using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Describes provider status text stored in one contiguous native buffer.</summary>
/// <remarks>
/// Description and device identifier fields are byte offsets and lengths relative to the start
/// of this structure. The complete buffer must remain valid for the native call that consumes it.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CfSyncStatus
{
    /// <summary>Total size of the structure and its trailing data in bytes.</summary>
    public uint StructSize;

    /// <summary>Provider-defined status code.</summary>
    public uint Code;

    /// <summary>Byte offset of the status description from the start of this structure.</summary>
    public uint DescriptionOffset;

    /// <summary>Length of the status description in bytes.</summary>
    public uint DescriptionLength;

    /// <summary>Byte offset of the device identifier from the start of this structure.</summary>
    public uint DeviceIdOffset;

    /// <summary>Length of the device identifier in bytes.</summary>
    public uint DeviceIdLength;
}
