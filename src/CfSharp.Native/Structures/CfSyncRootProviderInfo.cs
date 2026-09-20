using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Contains the current status and display information of a sync-root provider.
/// </summary>
/// <remarks>
/// <para>
/// This fixed-size structure is the blittable managed representation of
/// <c>CF_SYNC_ROOT_PROVIDER_INFO</c>. The UTF-16 buffers are null-terminated when the
/// corresponding text occupies fewer than <see cref="CfApi.MaxProviderNameLength"/> or
/// <see cref="CfApi.MaxProviderVersionLength"/> characters.
/// </para>
/// <para>The value owns no native resources and remains valid after the query returns.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfSyncRootProviderInfo
{
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
}
