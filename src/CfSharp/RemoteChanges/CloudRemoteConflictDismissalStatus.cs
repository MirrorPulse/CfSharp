namespace CfSharp;

/// <summary>Describes the durable outcome of preserving local content and dismissing a remote conflict.</summary>
/// <remarks>No value claims that local content has been uploaded or accepted by the remote service.</remarks>
public enum CloudRemoteConflictDismissalStatus
{
    /// <summary>The unresolved conflict was removed and a durable dismissal receipt was committed.</summary>
    Dismissed = 0,

    /// <summary>A previous call durably dismissed this identifier; no additional work occurred.</summary>
    AlreadyDismissed = 1,

    /// <summary>No unresolved conflict or dismissal receipt exists, including conflicts resolved by other APIs.</summary>
    NotFound = 2,
}
