using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>
/// Describes why Windows cancelled an outstanding callback request.
/// </summary>
[Flags]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The name intentionally mirrors the native CF_CALLBACK_CANCEL_FLAGS type.")]
public enum CfCallbackCancelFlags : uint
{
    /// <summary>The cancellation has no additional reason flags.</summary>
    None = 0x00000000,

    /// <summary>The associated I/O request timed out.</summary>
    IoTimeout = 0x00000001,

    /// <summary>The associated I/O request was aborted.</summary>
    IoAborted = 0x00000002,
}
