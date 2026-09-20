using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Contains basic placeholder state and a variable-length trailing identity.</summary>
/// <remarks>
/// <para>
/// This structure ends with the first byte of a variable-length identity. Allocate a native
/// buffer using the byte count returned by <see cref="CfApi.CfGetPlaceholderInfo"/> and read
/// exactly <see cref="FileIdentityLength"/> bytes beginning at <see cref="FileIdentity"/>.
/// </para>
/// <para>The structure owns no memory; the query buffer remains caller-owned.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfPlaceholderBasicInfo
{
    /// <summary>User-requested local-availability state.</summary>
    public CfPinState PinState;

    /// <summary>Whether the placeholder agrees with provider state.</summary>
    public CfInSyncState InSyncState;

    /// <summary>Volume-wide identifier of the item.</summary>
    public long FileId;

    /// <summary>Volume-wide identifier of the containing sync root.</summary>
    public long SyncRootFileId;

    /// <summary>Number of valid bytes beginning at <see cref="FileIdentity"/>.</summary>
    public uint FileIdentityLength;

    /// <summary>First byte of the variable-length provider identity.</summary>
    public fixed byte FileIdentity[1];
}

/// <summary>Contains storage accounting, state, identifiers, and a trailing placeholder identity.</summary>
/// <remarks>
/// This structure has a variable-length tail. Its fixed managed size includes one identity byte
/// and native alignment padding; use the returned native byte count and
/// <see cref="FileIdentityLength"/> rather than treating it as a standalone value.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfPlaceholderStandardInfo
{
    /// <summary>Total number of content bytes physically present on disk.</summary>
    public long OnDiskDataSize;

    /// <summary>Number of on-disk bytes validated against provider state.</summary>
    public long ValidatedDataSize;

    /// <summary>Number of on-disk bytes modified locally.</summary>
    public long ModifiedDataSize;

    /// <summary>Total on-disk bytes used by placeholder properties.</summary>
    public long PropertiesSize;

    /// <summary>User-requested local-availability state.</summary>
    public CfPinState PinState;

    /// <summary>Whether the placeholder agrees with provider state.</summary>
    public CfInSyncState InSyncState;

    /// <summary>Volume-wide identifier of the item.</summary>
    public long FileId;

    /// <summary>Volume-wide identifier of the containing sync root.</summary>
    public long SyncRootFileId;

    /// <summary>Number of valid bytes beginning at <see cref="FileIdentity"/>.</summary>
    public uint FileIdentityLength;

    /// <summary>First byte of the variable-length provider identity.</summary>
    public fixed byte FileIdentity[1];
}
