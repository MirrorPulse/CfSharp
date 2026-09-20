namespace CfSharp.Native;

/// <summary>
/// Adds optional behavior to a primary population policy.
/// </summary>
/// <remarks>
/// The current Cloud Files API defines no nonzero population modifiers. The separate
/// 16-bit type is retained to preserve the native structure contract and future expansion.
/// </remarks>
[Flags]
public enum CfPopulationPolicyModifier : ushort
{
    /// <summary>Applies no population-policy modifiers.</summary>
    None = 0x0000,
}
