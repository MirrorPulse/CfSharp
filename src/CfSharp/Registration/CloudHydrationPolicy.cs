namespace CfSharp;

/// <summary>
/// Specifies how Windows hydrates placeholder file content for user I/O.
/// </summary>
public enum CloudHydrationPolicy
{
    /// <summary>Hydrates only the ranges required by the current user request.</summary>
    Partial = 0,

    /// <summary>
    /// Satisfies requested ranges promptly and continues hydrating remaining content while
    /// a user handle remains open.
    /// </summary>
    Progressive = 1,

    /// <summary>Hydrates the entire file before completing the user request.</summary>
    Full = 2,

    /// <summary>Requires placeholders to remain fully hydrated.</summary>
    AlwaysFull = 3,
}
