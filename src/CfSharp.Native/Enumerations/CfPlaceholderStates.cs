using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>Represents the user's requested local-availability state for a placeholder.</summary>
public enum CfPinState
{
    /// <summary>No explicit pin state is recorded.</summary>
    Unspecified = 0,

    /// <summary>The item should remain available locally.</summary>
    Pinned = 1,

    /// <summary>The item may be dehydrated when local storage is needed.</summary>
    Unpinned = 2,

    /// <summary>The item is excluded from synchronization.</summary>
    Excluded = 3,

    /// <summary>The item should inherit its pin state from its parent. Valid only when setting state.</summary>
    Inherit = 4,
}

/// <summary>Controls recursive application of a placeholder pin state.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_SET_PIN_FLAGS.")]
public enum CfSetPinFlags : uint
{
    /// <summary>Applies the state only to the supplied handle.</summary>
    None = 0,

    /// <summary>Applies the state to the directory and all descendants.</summary>
    Recurse = 0x00000001,

    /// <summary>Applies the state to descendants but not to the supplied directory.</summary>
    RecurseOnly = 0x00000002,

    /// <summary>Stops recursive processing at the first error.</summary>
    RecurseStopOnError = 0x00000004,
}

/// <summary>Represents whether placeholder metadata and content agree with provider state.</summary>
public enum CfInSyncState
{
    /// <summary>The placeholder differs from provider state.</summary>
    NotInSync = 0,

    /// <summary>The placeholder agrees with provider state.</summary>
    InSync = 1,
}

/// <summary>Controls an in-sync state update.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_SET_IN_SYNC_FLAGS.")]
public enum CfSetInSyncFlags : uint
{
    /// <summary>Uses the only behavior currently defined by the platform.</summary>
    None = 0,
}
