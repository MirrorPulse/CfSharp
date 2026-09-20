namespace CfSharp;

/// <summary>Describes the process lifecycle of a <see cref="CloudFileSystem"/> instance.</summary>
public enum CloudFileSystemLifecycleState
{
    /// <summary>The immutable configuration is built but no store or provider session is open.</summary>
    Created = 0,

    /// <summary>The state store and Windows provider session are being opened.</summary>
    Starting = 1,

    /// <summary>The state store is owned and the sync root is ready for file-system operations.</summary>
    Started = 2,

    /// <summary>
    /// Owned process resources are being released, or a prior disposal attempt must be retried.
    /// </summary>
    Stopping = 3,

    /// <summary>All owned process resources have been released.</summary>
    Disposed = 4,
}
