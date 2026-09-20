using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Contains basic file-system timestamps, attributes, and logical file size.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfFsMetadata
{
    /// <summary>Basic timestamps and Win32 file attributes.</summary>
    public CfFileBasicInfo BasicInfo;

    /// <summary>Logical file size in bytes. Use zero for directories.</summary>
    public long FileSize;
}

/// <summary>Mirrors the Windows <c>FILE_BASIC_INFO</c> structure used by Cloud Files.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfFileBasicInfo
{
    /// <summary>Creation time as a native 100-nanosecond file-time value.</summary>
    public long CreationTime;

    /// <summary>Last access time as a native 100-nanosecond file-time value.</summary>
    public long LastAccessTime;

    /// <summary>Last write time as a native 100-nanosecond file-time value.</summary>
    public long LastWriteTime;

    /// <summary>Change time as a native 100-nanosecond file-time value.</summary>
    public long ChangeTime;

    /// <summary>Win32 file-attribute bits.</summary>
    public uint FileAttributes;
}
