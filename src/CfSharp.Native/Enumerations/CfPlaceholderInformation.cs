using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>Selects the result layout returned by <see cref="CfApi.CfGetPlaceholderInfo"/>.</summary>
public enum CfPlaceholderInfoClass
{
    /// <summary>Returns <see cref="CfPlaceholderBasicInfo"/> and its trailing identity.</summary>
    Basic = 0,

    /// <summary>Returns <see cref="CfPlaceholderStandardInfo"/> and its trailing identity.</summary>
    Standard = 1,
}

/// <summary>Selects a category of placeholder byte ranges.</summary>
public enum CfPlaceholderRangeInfoClass
{
    /// <summary>Returns all ranges physically present on disk.</summary>
    OnDisk = 1,

    /// <summary>Returns on-disk ranges whose content has been validated against provider state.</summary>
    Validated = 2,

    /// <summary>Returns on-disk ranges modified locally since provider validation.</summary>
    Modified = 3,
}

/// <summary>Describes the Cloud Files state inferred for a file or directory.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_PLACEHOLDER_STATE.")]
public enum CfPlaceholderState : uint
{
    /// <summary>The item has no Cloud Files state.</summary>
    None = 0,

    /// <summary>The item is a Cloud Files placeholder.</summary>
    Placeholder = 0x00000001,

    /// <summary>The item is a registered Cloud Files sync root.</summary>
    SyncRoot = 0x00000002,

    /// <summary>The placeholder contains an essential property.</summary>
    EssentialPropertyPresent = 0x00000004,

    /// <summary>The placeholder agrees with provider state.</summary>
    InSync = 0x00000008,

    /// <summary>The placeholder content is not yet ready for ordinary consumption.</summary>
    Partial = 0x00000010,

    /// <summary>Only part of the placeholder content is physically present.</summary>
    PartiallyOnDisk = 0x00000020,

    /// <summary>The supplied file information could not be interpreted.</summary>
    Invalid = uint.MaxValue,
}
