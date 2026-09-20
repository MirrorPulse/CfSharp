using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Provides the provider and identity data used to register a sync root.
/// </summary>
/// <remarks>
/// <para>
/// This is the blittable managed representation of <c>CF_SYNC_REGISTRATION</c>. Before
/// registration, <see cref="StructSize"/> must be set to the native size of this structure.
/// </para>
/// <para>
/// All pointer fields are borrowed. Their targets must remain valid and pinned for the
/// complete <see cref="CfApi.CfRegisterSyncRoot"/> call. Windows persistently copies the
/// strings and identity blobs before that call returns; callers retain ownership and must
/// release or unpin their memory afterward.
/// </para>
/// <para>
/// Instances are not thread-safe while being mutated. Separate initialized values may be
/// used concurrently for independent registration calls.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfSyncRegistration
{
    /// <summary>
    /// Size of this structure in bytes. Set this to <c>sizeof(CfSyncRegistration)</c> before use.
    /// </summary>
    public uint StructSize;

    /// <summary>
    /// Pointer to a null-terminated UTF-16 provider display name of at most 255 characters.
    /// </summary>
    public char* ProviderName;

    /// <summary>
    /// Pointer to a null-terminated UTF-16 provider version of at most 255 characters.
    /// </summary>
    public char* ProviderVersion;

    /// <summary>
    /// Pointer to an optional provider-defined sync-root identity of at most 64 KiB.
    /// Set this to <see langword="null"/> when <see cref="SyncRootIdentityLength"/> is zero.
    /// </summary>
    public void* SyncRootIdentity;

    /// <summary>Length of <see cref="SyncRootIdentity"/> in bytes.</summary>
    public uint SyncRootIdentityLength;

    /// <summary>
    /// Pointer to an optional provider-defined file identity for the root placeholder of at
    /// most <see cref="CfApi.MaxFileIdentityLength"/> bytes. Set this to
    /// <see langword="null"/> when <see cref="FileIdentityLength"/> is zero.
    /// </summary>
    public void* FileIdentity;

    /// <summary>Length of <see cref="FileIdentity"/> in bytes.</summary>
    public uint FileIdentityLength;

    /// <summary>
    /// Stable provider identifier used for telemetry correlation. A zero GUID asks Windows
    /// to derive an identifier from <see cref="ProviderName"/>.
    /// </summary>
    public Guid ProviderId;
}
