namespace CfSharp.Native;

/// <summary>
/// Identifies a Cloud Files callback and the corresponding parameter-union member.
/// </summary>
public enum CfCallbackType
{
    /// <summary>Requests file content for a dehydrated placeholder.</summary>
    FetchData = 0,

    /// <summary>Requests validation of previously transferred placeholder content.</summary>
    ValidateData = 1,

    /// <summary>Cancels an outstanding file-content request.</summary>
    CancelFetchData = 2,

    /// <summary>Requests placeholder namespace entries for a directory.</summary>
    FetchPlaceholders = 3,

    /// <summary>Cancels an outstanding placeholder-enumeration request.</summary>
    CancelFetchPlaceholders = 4,

    /// <summary>Notifies the provider after a placeholder has been opened.</summary>
    NotifyFileOpenCompletion = 5,

    /// <summary>Notifies the provider after a placeholder has been closed.</summary>
    NotifyFileCloseCompletion = 6,

    /// <summary>Requests approval before a placeholder is dehydrated.</summary>
    NotifyDehydrate = 7,

    /// <summary>Notifies the provider after dehydration completes.</summary>
    NotifyDehydrateCompletion = 8,

    /// <summary>Requests approval before a placeholder is deleted.</summary>
    NotifyDelete = 9,

    /// <summary>Notifies the provider after deletion completes.</summary>
    NotifyDeleteCompletion = 10,

    /// <summary>Requests approval before a placeholder is renamed or moved.</summary>
    NotifyRename = 11,

    /// <summary>Notifies the provider after a rename or move completes.</summary>
    NotifyRenameCompletion = 12,

    /// <summary>Terminates a <see cref="CfCallbackRegistration"/> array.</summary>
    None = -1,
}
