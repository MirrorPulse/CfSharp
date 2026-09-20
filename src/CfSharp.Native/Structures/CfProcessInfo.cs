using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Describes the process whose I/O caused a Cloud Files callback.
/// </summary>
/// <remarks>
/// Every pointer is borrowed callback memory and is valid only for the callback invocation.
/// Process information is supplied only when the connection requests it.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfProcessInfo
{
    /// <summary>Size of the native structure in bytes.</summary>
    public uint StructSize;

    /// <summary>Identifier of the process that initiated the operation.</summary>
    public uint ProcessId;

    /// <summary>Pointer to the null-terminated UTF-16 process image path.</summary>
    public char* ImagePath;

    /// <summary>Pointer to the null-terminated UTF-16 package name, when available.</summary>
    public char* PackageName;

    /// <summary>Pointer to the null-terminated UTF-16 application identifier, when available.</summary>
    public char* ApplicationId;

    /// <summary>Pointer to the null-terminated UTF-16 command line, when available.</summary>
    public char* CommandLine;

    /// <summary>Windows session identifier of the initiating process.</summary>
    public uint SessionId;
}
