using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>Flags for a data-transfer operation.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_OPERATION_TRANSFER_DATA_FLAGS.")]
public enum CfOperationTransferDataFlags : uint
{
    /// <summary>Uses the default behavior.</summary>
    None = 0,
}

/// <summary>Flags for a data-retrieval operation.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_OPERATION_RETRIEVE_DATA_FLAGS.")]
public enum CfOperationRetrieveDataFlags : uint
{
    /// <summary>Uses the default behavior.</summary>
    None = 0,
}

/// <summary>Flags for a data-acknowledgement operation.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_OPERATION_ACK_DATA_FLAGS.")]
public enum CfOperationAckDataFlags : uint
{
    /// <summary>Uses the default behavior.</summary>
    None = 0,
}

/// <summary>Flags for restarting hydration.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_OPERATION_RESTART_HYDRATION_FLAGS.")]
public enum CfOperationRestartHydrationFlags : uint
{
    /// <summary>Uses the default behavior.</summary>
    None = 0,

    /// <summary>Marks the placeholder as in sync while restarting hydration.</summary>
    MarkInSync = 0x00000001,
}

/// <summary>Flags for transferring child placeholders.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS.")]
public enum CfOperationTransferPlaceholdersFlags : uint
{
    /// <summary>Uses the default behavior.</summary>
    None = 0,

    /// <summary>Stops processing after the first failed placeholder entry.</summary>
    StopOnError = 0x00000001,

    /// <summary>Disables on-demand population for the callback target directory.</summary>
    DisableOnDemandPopulation = 0x00000002,
}

/// <summary>Flags for a dehydration acknowledgement.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_OPERATION_ACK_DEHYDRATE_FLAGS.")]
public enum CfOperationAckDehydrateFlags : uint
{
    /// <summary>Uses the default behavior.</summary>
    None = 0,
}

/// <summary>Flags for a rename acknowledgement.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_OPERATION_ACK_RENAME_FLAGS.")]
public enum CfOperationAckRenameFlags : uint
{
    /// <summary>Uses the default behavior.</summary>
    None = 0,
}

/// <summary>Flags for a delete acknowledgement.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_OPERATION_ACK_DELETE_FLAGS.")]
public enum CfOperationAckDeleteFlags : uint
{
    /// <summary>Uses the default behavior.</summary>
    None = 0,
}
