using System.Runtime.Versioning;

namespace CfSharp.Native;

[SupportedOSPlatform("windows10.0.16299")]
internal static unsafe class NativeSyncRoot
{
    private const int MaxSyncRootIdentityLength = 64 * 1024;

    internal static int Register(
        string path,
        string providerName,
        string providerVersion,
        Guid providerId,
        ReadOnlySpan<byte> syncRootIdentity,
        ReadOnlySpan<byte> fileIdentity,
        CfSyncPolicies policies,
        CfRegisterFlags flags)
    {
        fixed (char* pathPointer = path)
        fixed (char* providerNamePointer = providerName)
        fixed (char* providerVersionPointer = providerVersion)
        fixed (byte* syncRootIdentityPointer = syncRootIdentity)
        fixed (byte* fileIdentityPointer = fileIdentity)
        {
            CfSyncRegistration registration = new()
            {
                StructSize = (uint)sizeof(CfSyncRegistration),
                ProviderName = providerNamePointer,
                ProviderVersion = providerVersionPointer,
                SyncRootIdentity = syncRootIdentityPointer,
                SyncRootIdentityLength = (uint)syncRootIdentity.Length,
                FileIdentity = fileIdentityPointer,
                FileIdentityLength = (uint)fileIdentity.Length,
                ProviderId = providerId,
            };

            return CfApi.CfRegisterSyncRoot(pathPointer, &registration, &policies, flags);
        }
    }

    internal static int Query(string path, out NativeSyncRootInfo? info)
    {
        CfSyncRootStandardInfo layout = default;
        int identityOffset = checked((int)(layout.SyncRootIdentity - (byte*)&layout));
        byte[] buffer = GC.AllocateUninitializedArray<byte>(
            identityOffset + MaxSyncRootIdentityLength);

        fixed (char* pathPointer = path)
        fixed (byte* bufferPointer = buffer)
        {
            uint returnedLength;
            int result = CfApi.CfGetSyncRootInfoByPath(
                pathPointer,
                CfSyncRootInfoClass.Standard,
                bufferPointer,
                (uint)buffer.Length,
                &returnedLength);
            if (result < 0)
            {
                info = null;
                return result;
            }

            CfSyncRootStandardInfo* nativeInfo = (CfSyncRootStandardInfo*)bufferPointer;
            int identityLength = checked((int)nativeInfo->SyncRootIdentityLength);
            if (identityLength > MaxSyncRootIdentityLength ||
                identityOffset + identityLength > buffer.Length ||
                identityOffset + identityLength > returnedLength)
            {
                throw new InvalidDataException(
                    "Windows returned an invalid variable-length sync-root identity.");
            }

            info = new NativeSyncRootInfo(
                nativeInfo->SyncRootFileId,
                nativeInfo->HydrationPolicy,
                nativeInfo->PopulationPolicy,
                nativeInfo->InSyncPolicy,
                nativeInfo->HardLinkPolicy,
                nativeInfo->ProviderStatus,
                new string(nativeInfo->ProviderName),
                new string(nativeInfo->ProviderVersion),
                new ReadOnlySpan<byte>(nativeInfo->SyncRootIdentity, identityLength).ToArray());
            return result;
        }
    }

    internal static int Unregister(string path)
    {
        fixed (char* pathPointer = path)
        {
            return CfApi.CfUnregisterSyncRoot(pathPointer);
        }
    }
}

internal sealed record NativeSyncRootInfo(
    long FileId,
    CfHydrationPolicy HydrationPolicy,
    CfPopulationPolicy PopulationPolicy,
    CfInSyncPolicy InSyncPolicy,
    CfHardLinkPolicy HardLinkPolicy,
    CfSyncProviderStatus ProviderStatus,
    string ProviderName,
    string ProviderVersion,
    byte[] SyncRootIdentity);
