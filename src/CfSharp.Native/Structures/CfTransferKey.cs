using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Identifies the file stream associated with a Cloud Files transfer.</summary>
/// <remarks>
/// This opaque value is supplied by Windows in callback information and is valid only for
/// operations associated with that callback request. It owns no independently releasable resource.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CfTransferKey
{
    /// <summary>Contains the platform-owned opaque key value.</summary>
    public long Internal;
}
