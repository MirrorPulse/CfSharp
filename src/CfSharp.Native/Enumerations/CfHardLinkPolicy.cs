namespace CfSharp.Native;

/// <summary>
/// Controls whether placeholders may participate in file-system hard links.
/// </summary>
[Flags]
public enum CfHardLinkPolicy : uint
{
    /// <summary>Disallows hard links for placeholders.</summary>
    None = 0x00000000,

    /// <summary>Allows hard links when the provider can maintain their synchronization semantics.</summary>
    Allowed = 0x00000001,
}
