using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using CfSharp.Native;

namespace CfSharp;

internal sealed record CloudPlaceholderNativeEntryResult(int HResult, long CreateUsn);

internal sealed class CloudPlaceholderNativeBatchResult
{
    internal CloudPlaceholderNativeBatchResult(
        int hresult,
        uint entriesProcessed,
        IEnumerable<CloudPlaceholderNativeEntryResult> entries)
    {
        HResult = hresult;
        EntriesProcessed = entriesProcessed;
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    internal int HResult { get; }

    internal uint EntriesProcessed { get; }

    internal IReadOnlyList<CloudPlaceholderNativeEntryResult> Entries { get; }
}

[SupportedOSPlatform("windows10.0.16299")]
internal static class CloudPlaceholderPlatform
{
    internal static unsafe CloudPlaceholderNativeBatchResult CreatePlaceholders(
        string baseDirectoryPath,
        IReadOnlyList<CloudPlaceholderSpec> specifications,
        bool stopOnFirstFailure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectoryPath);
        ArgumentNullException.ThrowIfNull(specifications);
        if (specifications.Count == 0)
        {
            throw new ArgumentException(
                "At least one placeholder specification is required.",
                nameof(specifications));
        }

        CfPlaceholderCreateInfo[] nativeEntries = new CfPlaceholderCreateInfo[specifications.Count];
        List<GCHandle> pinnedBuffers = new(specifications.Count * 2);
        try
        {
            for (int index = 0; index < specifications.Count; index++)
            {
                CloudPlaceholderSpec specification = specifications[index];
                char[] name = (specification.Name + '\0').ToCharArray();
                byte[] identity = specification.Identity.Encode();
                GCHandle nameHandle = GCHandle.Alloc(name, GCHandleType.Pinned);
                pinnedBuffers.Add(nameHandle);
                GCHandle identityHandle = GCHandle.Alloc(identity, GCHandleType.Pinned);
                pinnedBuffers.Add(identityHandle);

                nativeEntries[index] = new CfPlaceholderCreateInfo
                {
                    RelativeFileName = (char*)nameHandle.AddrOfPinnedObject(),
                    FsMetadata = CreateMetadata(specification),
                    FileIdentity = (void*)identityHandle.AddrOfPinnedObject(),
                    FileIdentityLength = checked((uint)identity.Length),
                    Flags = CreateFlags(specification),
                };
            }

            fixed (char* baseDirectoryPointer = baseDirectoryPath)
            fixed (CfPlaceholderCreateInfo* entriesPointer = nativeEntries)
            {
                uint entriesProcessed;
                int hresult = CfApi.CfCreatePlaceholders(
                    baseDirectoryPointer,
                    entriesPointer,
                    checked((uint)nativeEntries.Length),
                    stopOnFirstFailure ? CfCreateFlags.StopOnError : CfCreateFlags.None,
                    &entriesProcessed);
                CloudPlaceholderNativeEntryResult[] results = nativeEntries
                    .Select(static entry => new CloudPlaceholderNativeEntryResult(
                        entry.Result,
                        entry.CreateUsn))
                    .ToArray();
                return new CloudPlaceholderNativeBatchResult(
                    hresult,
                    entriesProcessed,
                    results);
            }
        }
        finally
        {
            for (int index = pinnedBuffers.Count - 1; index >= 0; index--)
            {
                pinnedBuffers[index].Free();
            }
        }
    }

    internal static unsafe void SetInitialPinState(
        string path,
        CloudAvailabilityTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SafeCloudFilesProtectedHandle protectedHandle = SafeCloudFilesProtectedHandle.Open(
            path,
            CfOpenFileFlags.Exclusive | CfOpenFileFlags.WriteAccess,
            "CloudDirectory.CreatePlaceholders.Open");
        using SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
            protectedHandle.AcquireReference();

        CfPinState pinState = target is CloudAvailabilityTarget.AlwaysAvailable
            ? CfPinState.Pinned
            : CfPinState.Unpinned;
        int pinResult = CfApi.CfSetPinState(
            handle.Win32Handle,
            pinState,
            CfSetPinFlags.None,
            overlapped: null);
        if (pinResult < 0)
        {
            throw CloudFilesException.FromHResult(
                "CloudDirectory.CreatePlaceholders.SetPinState",
                path,
                pinResult);
        }

    }

    internal static unsafe void Hydrate(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SafeCloudFilesProtectedHandle protectedHandle = SafeCloudFilesProtectedHandle.Open(
            path,
            CfOpenFileFlags.Foreground,
            "CloudDirectory.CreatePlaceholders.OpenForHydration");
        using SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
            protectedHandle.AcquireReference();
        int hydrateResult = CfApi.CfHydratePlaceholder(
            handle.Win32Handle,
            startingOffset: 0,
            CfApi.EndOfFile,
            CfHydrateFlags.None,
            overlapped: null);
        if (hydrateResult < 0)
        {
            throw CloudFilesException.FromHResult(
                "CloudDirectory.CreatePlaceholders.Hydrate",
                path,
                hydrateResult);
        }
    }

    internal static CfFsMetadata CreateMetadata(CloudPlaceholderSpec specification)
    {
        CloudPlaceholderMetadata metadata = specification.Metadata;
        return new CfFsMetadata
        {
            BasicInfo = new CfFileBasicInfo
            {
                CreationTime = metadata.CreationTime?.ToFileTime() ?? 0,
                LastAccessTime = metadata.LastAccessTime?.ToFileTime() ?? 0,
                LastWriteTime = metadata.LastWriteTime?.ToFileTime() ?? 0,
                ChangeTime = metadata.ChangeTime?.ToFileTime() ?? 0,
                FileAttributes = (uint)metadata.Attributes,
            },
            FileSize = specification is CloudFilePlaceholderSpec file ? file.Length : 0,
        };
    }

    internal static CfPlaceholderCreateFlags CreateFlags(CloudPlaceholderSpec specification)
    {
        CfPlaceholderCreateFlags flags = CfPlaceholderCreateFlags.None;
        if (specification.InitiallyInSync)
        {
            flags |= CfPlaceholderCreateFlags.MarkInSync;
        }

        if (specification.CollisionBehavior is CloudPlaceholderCollisionBehavior.Supersede)
        {
            flags |= CfPlaceholderCreateFlags.Supersede;
        }

        if (specification is CloudDirectoryPlaceholderSpec
            {
                PopulationState: CloudDirectoryPopulationState.Complete,
            })
        {
            flags |= CfPlaceholderCreateFlags.DisableOnDemandPopulation;
        }

        if (specification is CloudFilePlaceholderSpec
            {
                InitialAvailability: CloudAvailabilityTarget.AlwaysAvailable,
            })
        {
            flags |= CfPlaceholderCreateFlags.AlwaysFull;
        }

        return flags;
    }
}
