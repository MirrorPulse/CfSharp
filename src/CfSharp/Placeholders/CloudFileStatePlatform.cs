using System.Runtime.Versioning;

using CfSharp.Native;

namespace CfSharp;

[SupportedOSPlatform("windows10.0.16299")]
internal static class CloudFileStatePlatform
{
    private const int MoreDataHResult = unchecked((int)0x800700EA);
    private const int HandleEofHResult = unchecked((int)0x80070026);
    private const int InitialRangeCapacity = 16;
    private const int MaximumRangeCapacity = 1 << 20;

    internal static unsafe void SetPinState(string path, CloudPinTarget target)
    {
        using SafeCloudFilesProtectedHandle protectedHandle = SafeCloudFilesProtectedHandle.Open(
            path,
            CfOpenFileFlags.Exclusive | CfOpenFileFlags.WriteAccess,
            "CloudItem.SetPinState.Open");
        using SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
            protectedHandle.AcquireReference();
        int result = CfApi.CfSetPinState(
            handle.Win32Handle,
            (CfPinState)target,
            CfSetPinFlags.None,
            overlapped: null);
        ThrowIfFailed("CloudItem.SetPinState", path, result);
    }

    internal static unsafe long SetInSyncState(
        string path,
        bool inSync,
        long? expectedUsn)
    {
        using SafeCloudFilesProtectedHandle protectedHandle = SafeCloudFilesProtectedHandle.Open(
            path,
            CfOpenFileFlags.Foreground | CfOpenFileFlags.WriteAccess,
            "CloudItem.SetInSync.Open");
        using SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
            protectedHandle.AcquireReference();
        long operationUsn = expectedUsn ?? 0;
        int result = CfApi.CfSetInSyncState(
            handle.Win32Handle,
            inSync ? CfInSyncState.InSync : CfInSyncState.NotInSync,
            CfSetInSyncFlags.None,
            &operationUsn);
        ThrowIfFailed("CloudItem.SetInSync", path, result);
        return operationUsn;
    }

    internal static unsafe void Hydrate(string path, CloudFileRange range)
    {
        using SafeCloudFilesProtectedHandle protectedHandle = SafeCloudFilesProtectedHandle.Open(
            path,
            CfOpenFileFlags.Foreground,
            "CloudFile.Hydrate.Open");
        using SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
            protectedHandle.AcquireReference();
        int result = CfApi.CfHydratePlaceholder(
            handle.Win32Handle,
            range.Offset,
            GetNativeLength(range),
            CfHydrateFlags.None,
            overlapped: null);
        ThrowIfFailed("CloudFile.Hydrate", path, result);
    }

    internal static unsafe void Dehydrate(
        string path,
        CloudFileRange range,
        CloudDehydrationOptions options)
    {
        using SafeCloudFilesProtectedHandle protectedHandle = SafeCloudFilesProtectedHandle.Open(
            path,
            CfOpenFileFlags.Exclusive | CfOpenFileFlags.WriteAccess,
            "CloudFile.Dehydrate.Open");
        using SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
            protectedHandle.AcquireReference();
        int result = CfApi.CfDehydratePlaceholder(
            handle.Win32Handle,
            range.Offset,
            GetNativeLength(range),
            options.IsBackground ? CfDehydrateFlags.Background : CfDehydrateFlags.None,
            overlapped: null);
        ThrowIfFailed("CloudFile.Dehydrate", path, result);
    }

    internal static unsafe IReadOnlyList<CloudFileRange> GetRanges(
        string path,
        CloudPlaceholderRangeKind kind,
        CloudFileRange range,
        CancellationToken cancellationToken)
    {
        using SafeCloudFilesProtectedHandle protectedHandle = SafeCloudFilesProtectedHandle.Open(
            path,
            CfOpenFileFlags.None,
            "CloudFile.GetRanges.Open");
        using SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
            protectedHandle.AcquireReference();
        int capacity = InitialRangeCapacity;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CfFileRange[] nativeRanges = new CfFileRange[capacity];
            fixed (CfFileRange* rangesPointer = nativeRanges)
            {
                uint returnedLength;
                int result = CfApi.CfGetPlaceholderRangeInfo(
                    handle.Win32Handle,
                    (CfPlaceholderRangeInfoClass)((int)kind + 1),
                    range.Offset,
                    GetNativeLength(range),
                    rangesPointer,
                    checked((uint)(capacity * sizeof(CfFileRange))),
                    &returnedLength);
                if (result == HandleEofHResult)
                {
                    return Array.Empty<CloudFileRange>();
                }

                if (returnedLength > checked((uint)(capacity * sizeof(CfFileRange))) ||
                    returnedLength % sizeof(CfFileRange) != 0)
                {
                    throw new InvalidDataException(
                        "Windows returned an invalid placeholder range buffer length.");
                }

                if (result == MoreDataHResult)
                {
                    if (capacity >= MaximumRangeCapacity)
                    {
                        throw new InvalidDataException(
                            "The placeholder range result exceeded the supported safety limit.");
                    }

                    capacity = checked(Math.Min(capacity * 2, MaximumRangeCapacity));
                    continue;
                }

                ThrowIfFailed("CloudFile.GetRanges", path, result);
                int count = checked((int)(returnedLength / sizeof(CfFileRange)));
                return NormalizeRanges(nativeRanges.AsSpan(0, count));
            }
        }
    }

    private static IReadOnlyList<CloudFileRange> NormalizeRanges(
        ReadOnlySpan<CfFileRange> ranges)
    {
        if (ranges.IsEmpty)
        {
            return Array.Empty<CloudFileRange>();
        }

        List<CloudFileRange> ordered = new(ranges.Length);
        foreach (CfFileRange range in ranges)
        {
            if (range.StartingOffset < 0 || range.Length <= 0)
            {
                throw new InvalidDataException("Windows returned an invalid placeholder range.");
            }

            ordered.Add(new CloudFileRange(range.StartingOffset, range.Length));
        }

        ordered.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
        List<CloudFileRange> normalized = new(ordered.Count);
        foreach (CloudFileRange range in ordered)
        {
            if (normalized.Count == 0)
            {
                normalized.Add(range);
                continue;
            }

            CloudFileRange previous = normalized[^1];
            long previousEnd = checked(previous.Offset + previous.Length);
            if (range.Offset > previousEnd)
            {
                normalized.Add(range);
                continue;
            }

            long rangeEnd = checked(range.Offset + range.Length);
            long mergedEnd = Math.Max(previousEnd, rangeEnd);
            normalized[^1] = new CloudFileRange(previous.Offset, mergedEnd - previous.Offset);
        }

        return Array.AsReadOnly(normalized.ToArray());
    }

    private static long GetNativeLength(CloudFileRange range) =>
        range.ExtendsToEnd ? CfApi.EndOfFile : range.Length;

    private static void ThrowIfFailed(string operation, string path, int hresult)
    {
        if (hresult < 0)
        {
            throw CloudFilesException.FromHResult(operation, path, hresult);
        }
    }
}
