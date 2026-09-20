namespace CfSharp;

/// <summary>
/// Specifies whether placeholders may participate in file-system hard links.
/// </summary>
public enum CloudHardLinkPolicy
{
    /// <summary>Disallows hard links for placeholders.</summary>
    Disallowed = 0,

    /// <summary>Allows hard links when the provider maintains their synchronization semantics.</summary>
    Allowed = 1,
}
