namespace CfSharp;

/// <summary>
/// Specifies how Windows requests placeholder namespace entries during enumeration.
/// </summary>
public enum CloudPopulationPolicy
{
    /// <summary>Requests only entries needed by the current enumeration.</summary>
    Partial = 0,

    /// <summary>Requests the complete directory namespace when population is needed.</summary>
    Full = 2,

    /// <summary>Assumes the complete namespace is always available locally.</summary>
    AlwaysFull = 3,
}
