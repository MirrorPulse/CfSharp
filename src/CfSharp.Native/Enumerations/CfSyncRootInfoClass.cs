namespace CfSharp.Native;

/// <summary>
/// Selects the information returned by a sync-root query.
/// </summary>
public enum CfSyncRootInfoClass
{
    /// <summary>Returns a <see cref="CfSyncRootBasicInfo"/> value.</summary>
    Basic = 0,

    /// <summary>
    /// Returns a variable-length <see cref="CfSyncRootStandardInfo"/> value followed by
    /// the complete sync-root identity.
    /// </summary>
    Standard = 1,

    /// <summary>Returns a <see cref="CfSyncRootProviderInfo"/> value.</summary>
    Provider = 2,
}
