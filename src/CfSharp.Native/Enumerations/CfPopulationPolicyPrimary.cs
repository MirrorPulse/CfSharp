namespace CfSharp.Native;

/// <summary>
/// Specifies how the platform populates placeholder namespaces.
/// </summary>
/// <remarks>
/// This type uses a 16-bit underlying representation because <c>cfapi.h</c> stores the
/// primary value in <c>CF_POPULATION_POLICY_PRIMARY_USHORT</c> within the policy structure.
/// </remarks>
public enum CfPopulationPolicyPrimary : ushort
{
    /// <summary>Requests only the directory entries needed by the current enumeration.</summary>
    Partial = 0,

    /// <summary>Requests the complete directory namespace when population is needed.</summary>
    Full = 2,

    /// <summary>Assumes the complete namespace is always available locally.</summary>
    AlwaysFull = 3,
}
