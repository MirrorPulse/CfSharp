namespace CfSharp.Native;

/// <summary>
/// Identifies the reason associated with placeholder dehydration.
/// </summary>
public enum CfCallbackDehydrationReason
{
    /// <summary>No dehydration reason is available.</summary>
    None = 0,

    /// <summary>The user manually requested dehydration.</summary>
    UserManual = 1,

    /// <summary>Windows requested dehydration because local storage is low.</summary>
    SystemLowSpace = 2,

    /// <summary>Windows requested dehydration because content was inactive.</summary>
    SystemInactivity = 3,

    /// <summary>Windows requested dehydration during an operating-system upgrade.</summary>
    SystemOsUpgrade = 4,
}
