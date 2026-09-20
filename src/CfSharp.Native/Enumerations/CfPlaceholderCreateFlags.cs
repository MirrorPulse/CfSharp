using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>Controls how one entry in a placeholder-creation batch is created.</summary>
[Flags]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The name intentionally mirrors the native CF_PLACEHOLDER_CREATE_FLAGS type.")]
public enum CfPlaceholderCreateFlags : uint
{
    /// <summary>Uses the default placeholder creation behavior.</summary>
    None = 0x00000000,

    /// <summary>Disables on-demand population for the created placeholder directory.</summary>
    DisableOnDemandPopulation = 0x00000001,

    /// <summary>Marks the new placeholder as in sync.</summary>
    MarkInSync = 0x00000002,

    /// <summary>Replaces an existing file or directory with the placeholder.</summary>
    Supersede = 0x00000004,

    /// <summary>Creates a placeholder that must always retain its complete content.</summary>
    AlwaysFull = 0x00000008,
}
