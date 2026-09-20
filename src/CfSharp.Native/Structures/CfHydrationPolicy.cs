using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Combines the primary hydration behavior with optional modifiers.
/// </summary>
/// <remarks>
/// This is the blittable managed representation of <c>CF_HYDRATION_POLICY</c>. It owns
/// no native resources and may be copied freely.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CfHydrationPolicy
{
    /// <summary>Specifies how much content Windows hydrates for a user request.</summary>
    public CfHydrationPolicyPrimary Primary;

    /// <summary>Specifies validation, streaming, and automatic-dehydration behavior.</summary>
    public CfHydrationPolicyModifier Modifier;
}
