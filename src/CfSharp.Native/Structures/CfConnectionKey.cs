using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Identifies one active communication channel between a provider and a sync root.
/// </summary>
/// <remarks>
/// This opaque value is returned by <see cref="CfApi.CfConnectSyncRoot"/> and remains valid
/// until the connection is disconnected. It owns no independently releasable resource.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CfConnectionKey
{
    /// <summary>Contains the platform-owned opaque key value.</summary>
    public long Internal;
}
