namespace CfSharp;

/// <summary>
/// Identifies a versioned Windows Cloud Files capability whose availability must be checked
/// before a provider uses the corresponding native contract.
/// </summary>
public enum CloudFilesCapability
{
    /// <summary>Rich sync-root status reporting introduced by Windows 10 version 1803.</summary>
    RichSyncRootStatus,

    /// <summary>Provider progress V2 introduced by Windows 10 version 1809.</summary>
    ProviderProgressV2,

    /// <summary>Non-default placeholder management policy flags.</summary>
    PlaceholderManagementPolicy,

    /// <summary>The full-restart hydration policy modifier.</summary>
    FullRestartHydration,

    /// <summary>The force-convert-to-cloud-file placeholder flag.</summary>
    ForceConvertToCloudFile,

    /// <summary>Callback-key placeholder range information for hydration.</summary>
    PlaceholderRangeInfoForHydration,
}
