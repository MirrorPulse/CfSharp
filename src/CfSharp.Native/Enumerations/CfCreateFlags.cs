using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>Controls batch-level behavior for <see cref="CfApi.CfCreatePlaceholders"/>.</summary>
[Flags]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The name intentionally mirrors the native CF_CREATE_FLAGS type.")]
public enum CfCreateFlags : uint
{
    /// <summary>Attempts every placeholder entry even when an earlier entry fails.</summary>
    None = 0x00000000,

    /// <summary>Stops processing the batch after the first failed entry.</summary>
    StopOnError = 0x00000001,
}
