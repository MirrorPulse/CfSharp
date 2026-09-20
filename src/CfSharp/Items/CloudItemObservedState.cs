using System.Diagnostics.CodeAnalysis;

namespace CfSharp;

/// <summary>Describes how much file content is currently available from local storage.</summary>
public enum CloudContentAvailability
{
    /// <summary>The item is not a file placeholder, so Cloud Files availability does not apply.</summary>
    NotApplicable = 0,

    /// <summary>No usable file content is currently available without provider hydration.</summary>
    OnlineOnly = 1,

    /// <summary>Some, but not all, file content is currently present on disk.</summary>
    PartiallyAvailable = 2,

    /// <summary>The complete logical file content is currently available locally.</summary>
    FullyAvailable = 3,
}

/// <summary>Describes the user's observed pin intent for a Cloud Files placeholder.</summary>
public enum CloudPinState
{
    /// <summary>The item is not a placeholder or has no explicit pin intent.</summary>
    Unspecified = 0,

    /// <summary>The item is requested to remain available locally.</summary>
    Pinned = 1,

    /// <summary>The item may be dehydrated when local storage is needed.</summary>
    Unpinned = 2,

    /// <summary>The item is excluded from synchronization.</summary>
    Excluded = 3,
}

/// <summary>Describes whether an item currently agrees with provider state.</summary>
public enum CloudSynchronizationState
{
    /// <summary>The item has no Cloud Files synchronization state.</summary>
    NotApplicable = 0,

    /// <summary>The placeholder differs from the provider's acknowledged state.</summary>
    NotInSync = 1,

    /// <summary>The placeholder agrees with the provider's acknowledged state.</summary>
    InSync = 2,
}

/// <summary>Preserves independent Cloud Files state bits observed for an item.</summary>
[Flags]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The value describes a bitwise state snapshot, not an event-driven state machine.")]
public enum CloudPlaceholderState : uint
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

    /// <summary>The placeholder content is not ready for ordinary consumption.</summary>
    Partial = 0x00000010,

    /// <summary>Only part of the placeholder content is physically present.</summary>
    PartiallyOnDisk = 0x00000020,
}
