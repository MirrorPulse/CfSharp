using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Defines the Cloud Files policies assigned to a sync root during registration.
/// </summary>
/// <remarks>
/// <para>
/// This is the blittable managed representation of <c>CF_SYNC_POLICIES</c>. Before
/// registration, <see cref="StructSize"/> must be set to the native size of this structure.
/// </para>
/// <para>
/// The structure owns no resources. Callers may share immutable copies across threads,
/// but must synchronize concurrent mutation of the same instance.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CfSyncPolicies
{
    /// <summary>
    /// Size of this structure in bytes. Set this to <c>sizeof(CfSyncPolicies)</c> before use.
    /// </summary>
    public uint StructSize;

    /// <summary>Controls file-content hydration behavior.</summary>
    public CfHydrationPolicy Hydration;

    /// <summary>Controls directory namespace population behavior.</summary>
    public CfPopulationPolicy Population;

    /// <summary>Controls metadata changes that clear a placeholder's in-sync state.</summary>
    public CfInSyncPolicy InSync;

    /// <summary>Controls whether placeholders may participate in hard links.</summary>
    public CfHardLinkPolicy HardLink;

    /// <summary>Controls placeholder-management access by non-provider processes.</summary>
    public CfPlaceholderManagementPolicy PlaceholderManagement;
}
