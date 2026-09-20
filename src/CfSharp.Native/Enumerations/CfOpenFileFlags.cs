using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>Controls access, sharing, and oplock behavior for <see cref="CfApi.CfOpenFileWithOplock"/>.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_OPEN_FILE_FLAGS.")]
public enum CfOpenFileFlags : uint
{
    /// <summary>Opens with read access, full sharing, and a read-caching oplock.</summary>
    None = 0,

    /// <summary>
    /// Opens without sharing and requests read- and handle-caching oplocks so a conflicting
    /// foreground open can cause the protected handle to drain and close.
    /// </summary>
    Exclusive = 0x00000001,

    /// <summary>Requests file write-data or directory add-file access in addition to read access.</summary>
    WriteAccess = 0x00000002,

    /// <summary>Requests delete access.</summary>
    DeleteAccess = 0x00000004,

    /// <summary>
    /// Opens as a foreground caller without requesting an oplock. Existing oplocks may be broken.
    /// </summary>
    Foreground = 0x00000008,
}
