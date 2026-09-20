using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Identifies a byte range in a placeholder file.</summary>
/// <remarks>
/// For APIs that accept the native <c>CF_EOF</c> sentinel, set <see cref="Length"/> to
/// <see cref="CfApi.EndOfFile"/>. Range alignment requirements are defined by the consuming API.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CfFileRange
{
    /// <summary>Zero-based byte offset at which the range begins.</summary>
    public long StartingOffset;

    /// <summary>Length of the range in bytes, or an API-specific sentinel.</summary>
    public long Length;
}
