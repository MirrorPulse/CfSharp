using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Identifies one request delivered by the Cloud Files platform.</summary>
/// <remarks>
/// This opaque value is supplied by Windows and scopes request-specific operations such as
/// cancellation. It owns no independently releasable resource.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CfRequestKey
{
    /// <summary>Contains the platform-owned opaque key value.</summary>
    public long Internal;
}
