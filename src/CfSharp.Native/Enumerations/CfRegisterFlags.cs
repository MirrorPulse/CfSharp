using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>
/// Controls how <see cref="CfApi.CfRegisterSyncRoot"/> registers a sync root.
/// </summary>
/// <remarks>
/// Values may be combined. Unsupported combinations are rejected by the Cloud Files
/// platform with a failing <c>HRESULT</c>.
/// </remarks>
[Flags]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The name intentionally mirrors the native CF_REGISTER_FLAGS type.")]
public enum CfRegisterFlags : uint
{
    /// <summary>Uses the default registration behavior.</summary>
    None = 0x00000000,

    /// <summary>Updates the identities and policies of an existing registration.</summary>
    Update = 0x00000001,

    /// <summary>
    /// Disables on-demand population for the sync root directory itself while preserving
    /// the configured population policy for its descendants.
    /// </summary>
    DisableOnDemandPopulationOnRoot = 0x00000002,

    /// <summary>Marks the sync root directory as in sync during registration.</summary>
    MarkInSyncOnRoot = 0x00000004,
}
