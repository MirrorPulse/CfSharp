using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Contains the policies, provider information, and identity of a registered sync root.
/// </summary>
/// <remarks>
/// <para>
/// This is the variable-length native <c>CF_SYNC_ROOT_STANDARD_INFO</c> header. The
/// <see cref="SyncRootIdentity"/> field represents the first byte of trailing identity
/// storage. Callers must allocate the buffer length reported by the query and must not use
/// <c>sizeof(CfSyncRootStandardInfo)</c> as the complete result length when
/// <see cref="SyncRootIdentityLength"/> is greater than one.
/// </para>
/// <para>
/// The query buffer is caller-owned. No pointer into that buffer may outlive it, and callers
/// must synchronize access if the buffer is shared across threads.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfSyncRootStandardInfo
{
    /// <summary>File identifier assigned to the sync-root directory by its volume.</summary>
    public long SyncRootFileId;

    /// <summary>Hydration policy registered for the sync root.</summary>
    public CfHydrationPolicy HydrationPolicy;

    /// <summary>Population policy registered for the sync root.</summary>
    public CfPopulationPolicy PopulationPolicy;

    /// <summary>In-sync tracking policy registered for the sync root.</summary>
    public CfInSyncPolicy InSyncPolicy;

    /// <summary>Hard-link policy registered for the sync root.</summary>
    public CfHardLinkPolicy HardLinkPolicy;

    /// <summary>Current activity or terminal status reported for the provider.</summary>
    public CfSyncProviderStatus ProviderStatus;

    /// <summary>
    /// Fixed UTF-16 buffer containing the provider display name and a terminating null.
    /// </summary>
    public fixed char ProviderName[CfApi.MaxProviderNameLength + 1];

    /// <summary>
    /// Fixed UTF-16 buffer containing the provider version and a terminating null.
    /// </summary>
    public fixed char ProviderVersion[CfApi.MaxProviderVersionLength + 1];

    /// <summary>Length of the trailing provider-defined sync-root identity in bytes.</summary>
    public uint SyncRootIdentityLength;

    /// <summary>First byte of the variable-length provider-defined sync-root identity.</summary>
    public fixed byte SyncRootIdentity[1];
}
