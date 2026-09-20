namespace CfSharp.Native;

/// <summary>
/// Controls placeholder-management access while a sync provider is connected.
/// </summary>
/// <remarks>
/// Nonzero values require Cloud Files platform integration number <c>0x310</c> or later.
/// Values may be combined.
/// </remarks>
[Flags]
public enum CfPlaceholderManagementPolicy : uint
{
    /// <summary>Restricts placeholder management to the connected sync provider.</summary>
    Default = 0x00000000,

    /// <summary>Allows any process to create placeholders in an active sync root.</summary>
    CreateUnrestricted = 0x00000001,

    /// <summary>Allows any process to convert items to placeholders in an active sync root.</summary>
    ConvertToUnrestricted = 0x00000002,

    /// <summary>Allows any process to update placeholders in an active sync root.</summary>
    UpdateUnrestricted = 0x00000004,
}
