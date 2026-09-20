namespace CfSharp.Native;

/// <summary>
/// Specifies metadata changes that cause Windows to clear a placeholder's in-sync state.
/// </summary>
/// <remarks>
/// Content modification always clears the in-sync state. These flags control additional
/// metadata tracking and may be combined independently for files and directories.
/// </remarks>
[Flags]
public enum CfInSyncPolicy : uint
{
    /// <summary>Tracks no additional metadata.</summary>
    None = 0x00000000,

    /// <summary>Tracks file creation-time changes.</summary>
    TrackFileCreationTime = 0x00000001,

    /// <summary>Tracks changes to the read-only attribute on files.</summary>
    TrackFileReadOnlyAttribute = 0x00000002,

    /// <summary>Tracks changes to the hidden attribute on files.</summary>
    TrackFileHiddenAttribute = 0x00000004,

    /// <summary>Tracks changes to the system attribute on files.</summary>
    TrackFileSystemAttribute = 0x00000008,

    /// <summary>Tracks directory creation-time changes.</summary>
    TrackDirectoryCreationTime = 0x00000010,

    /// <summary>Tracks changes to the read-only attribute on directories.</summary>
    TrackDirectoryReadOnlyAttribute = 0x00000020,

    /// <summary>Tracks changes to the hidden attribute on directories.</summary>
    TrackDirectoryHiddenAttribute = 0x00000040,

    /// <summary>Tracks changes to the system attribute on directories.</summary>
    TrackDirectorySystemAttribute = 0x00000080,

    /// <summary>Tracks file last-write-time changes.</summary>
    TrackFileLastWriteTime = 0x00000100,

    /// <summary>Tracks directory last-write-time changes.</summary>
    TrackDirectoryLastWriteTime = 0x00000200,

    /// <summary>Tracks every file metadata category recognized by the platform.</summary>
    TrackFileAll = 0x0055550f,

    /// <summary>Tracks every directory metadata category recognized by the platform.</summary>
    TrackDirectoryAll = 0x00aaaaf0,

    /// <summary>Tracks every file and directory metadata category recognized by the platform.</summary>
    TrackAll = 0x00ffffff,

    /// <summary>
    /// Preserves the in-sync state for changes made by the connected sync provider.
    /// </summary>
    PreserveInSyncForSyncEngine = 0x80000000,
}
