using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Combines the primary namespace-population behavior with optional modifiers.
/// </summary>
/// <remarks>
/// This is the blittable managed representation of <c>CF_POPULATION_POLICY</c>. It owns
/// no native resources and may be copied freely.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CfPopulationPolicy
{
    /// <summary>Specifies how much namespace Windows requests during enumeration.</summary>
    public CfPopulationPolicyPrimary Primary;

    /// <summary>Specifies optional population behavior reserved for platform expansion.</summary>
    public CfPopulationPolicyModifier Modifier;
}
