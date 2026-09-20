using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>
/// Describes why Windows issued a file-content fetch callback.
/// </summary>
[Flags]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The name intentionally mirrors the native CF_CALLBACK_FETCH_DATA_FLAGS type.")]
public enum CfCallbackFetchDataFlags : uint
{
    /// <summary>The request has no additional characteristics.</summary>
    None = 0x00000000,

    /// <summary>The request is recovering hydration after an interrupted provider session.</summary>
    Recovery = 0x00000001,

    /// <summary>The request was initiated by an explicit hydration operation.</summary>
    ExplicitHydration = 0x00000002,
}
