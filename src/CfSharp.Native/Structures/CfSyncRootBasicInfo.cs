using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Contains the file-system identifier of a registered sync root.
/// </summary>
/// <remarks>
/// This is the blittable managed representation of <c>CF_SYNC_ROOT_BASIC_INFO</c>. The
/// value owns no native resources and remains valid after the query returns.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CfSyncRootBasicInfo
{
    /// <summary>File identifier assigned to the sync-root directory by its volume.</summary>
    public long SyncRootFileId;
}
