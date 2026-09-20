using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>
/// Requests additional callback information or connection behavior.
/// </summary>
[Flags]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The name intentionally mirrors the native CF_CONNECT_FLAGS type.")]
public enum CfConnectFlags : uint
{
    /// <summary>Uses the default connection behavior.</summary>
    None = 0x00000000,

    /// <summary>Requests process information in callback data.</summary>
    RequireProcessInfo = 0x00000002,

    /// <summary>Requests a full path rather than a sync-root-relative callback path.</summary>
    RequireFullFilePath = 0x00000004,

    /// <summary>Blocks implicit hydration initiated by activity from the provider process.</summary>
    BlockSelfImplicitHydration = 0x00000008,
}
