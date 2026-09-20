namespace CfSharp.Native;

/// <summary>
/// Adds optional behavior to a primary hydration policy.
/// </summary>
/// <remarks>
/// <see cref="ValidationRequired"/> and <see cref="StreamingAllowed"/> are mutually
/// exclusive. <see cref="AllowFullRestartHydration"/> requires Cloud Files platform
/// integration number <c>0x500</c> or later.
/// </remarks>
[Flags]
public enum CfHydrationPolicyModifier : ushort
{
    /// <summary>Applies no hydration-policy modifiers.</summary>
    None = 0x0000,

    /// <summary>Requires provider validation before hydrated data is returned to user I/O.</summary>
    ValidationRequired = 0x0001,

    /// <summary>Allows Windows to deliver provider data without persisting it locally.</summary>
    StreamingAllowed = 0x0002,

    /// <summary>Allows Windows to dehydrate in-sync placeholders automatically.</summary>
    AutoDehydrationAllowed = 0x0004,

    /// <summary>
    /// Allows full synchronous hydration when antivirus software scans a file whose
    /// provider may restart hydration with a different size.
    /// </summary>
    AllowFullRestartHydration = 0x0008,
}
