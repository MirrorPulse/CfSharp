namespace CfSharp.Native;

/// <summary>
/// Specifies how the platform hydrates placeholder file content.
/// </summary>
/// <remarks>
/// This type uses a 16-bit underlying representation because <c>cfapi.h</c> stores the
/// primary value in <c>CF_HYDRATION_POLICY_PRIMARY_USHORT</c> within the policy structure.
/// </remarks>
public enum CfHydrationPolicyPrimary : ushort
{
    /// <summary>Hydrates only ranges required by user I/O.</summary>
    Partial = 0,

    /// <summary>
    /// Satisfies requested ranges promptly and continues hydrating remaining content in
    /// the background while a user handle remains open.
    /// </summary>
    Progressive = 1,

    /// <summary>Hydrates the entire file before completing user I/O.</summary>
    Full = 2,

    /// <summary>Requires every placeholder to remain fully hydrated.</summary>
    AlwaysFull = 3,
}
