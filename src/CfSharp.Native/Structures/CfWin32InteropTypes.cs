using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Mirrors the Win32 <c>CORRELATION_VECTOR</c> structure consumed by CFAPI.</summary>
/// <remarks>
/// The vector is an ANSI, null-terminated string with at most 128 characters. Version 1 vectors
/// use at most 64 characters; version 2 vectors use the complete capacity. The caller owns this
/// inline buffer and no cleanup is required.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct CfCorrelationVector
{
    /// <summary>Correlation-vector format version. Windows currently defines versions 1 and 2.</summary>
    public byte Version;

    /// <summary>Inline null-terminated ANSI vector storage.</summary>
    public fixed byte Vector[129];
}

/// <summary>Mirrors the two-DWORD Win32 <c>FILETIME</c> layout.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfFileTime
{
    /// <summary>Low-order 32 bits of the 100-nanosecond timestamp.</summary>
    public uint LowDateTime;

    /// <summary>High-order 32 bits of the 100-nanosecond timestamp.</summary>
    public uint HighDateTime;
}

/// <summary>Mirrors the Unicode Win32 <c>WIN32_FIND_DATAW</c> structure consumed by CFAPI.</summary>
/// <remarks>
/// The structure is provided for <see cref="CfApi.CfGetPlaceholderStateFromFindData"/> and for
/// direct interoperation with <c>FindFirstFileW</c>/<c>FindNextFileW</c>. Both inline strings are
/// caller-owned fixed UTF-16 buffers.
/// </remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public unsafe struct CfWin32FindData
{
    /// <summary>Win32 file attribute bits.</summary>
    public uint FileAttributes;

    /// <summary>Creation time.</summary>
    public CfFileTime CreationTime;

    /// <summary>Last access time.</summary>
    public CfFileTime LastAccessTime;

    /// <summary>Last write time.</summary>
    public CfFileTime LastWriteTime;

    /// <summary>High-order 32 bits of the file size.</summary>
    public uint FileSizeHigh;

    /// <summary>Low-order 32 bits of the file size.</summary>
    public uint FileSizeLow;

    /// <summary>Reparse tag when <see cref="FileAttributes"/> contains the reparse-point bit.</summary>
    public uint Reserved0;

    /// <summary>Reserved Win32 value.</summary>
    public uint Reserved1;

    /// <summary>Primary null-terminated file name.</summary>
    public fixed char FileName[260];

    /// <summary>Legacy null-terminated short file name.</summary>
    public fixed char AlternateFileName[14];
}
