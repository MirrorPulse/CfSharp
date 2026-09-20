using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Describes one placeholder created by <see cref="CfApi.CfCreatePlaceholders"/>.</summary>
/// <remarks>
/// The name and identity pointers are caller-owned and need remain valid only until the call
/// returns. Windows writes <see cref="Result"/> and <see cref="CreateUsn"/> for this entry.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfPlaceholderCreateInfo
{
    /// <summary>Pointer to a null-terminated UTF-16 path relative to the base directory.</summary>
    public char* RelativeFileName;

    /// <summary>File-system metadata and logical size for the placeholder.</summary>
    public CfFsMetadata FsMetadata;

    /// <summary>Pointer to an optional provider-defined identity.</summary>
    public void* FileIdentity;

    /// <summary>Length of <see cref="FileIdentity"/> in bytes, limited to 4 KiB.</summary>
    public uint FileIdentityLength;

    /// <summary>Creation behavior for this entry.</summary>
    public CfPlaceholderCreateFlags Flags;

    /// <summary>Receives the per-entry <c>HRESULT</c>.</summary>
    public int Result;

    /// <summary>Receives the update sequence number assigned during creation.</summary>
    public long CreateUsn;
}
