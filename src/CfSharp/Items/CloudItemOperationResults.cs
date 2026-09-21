namespace CfSharp;

/// <summary>Describes a completed placeholder conversion, patch, or reversion.</summary>
/// <remarks>
/// The result owns no native resources. Its snapshot was captured after native and durable-state
/// work completed and is safe for concurrent reads. The path-bound item reference itself remains
/// immutable.
/// </remarks>
public sealed class CloudPlaceholderMutationResult
{
    internal CloudPlaceholderMutationResult(
        string path,
        long? operationUsn,
        CloudItemSnapshot snapshot,
        bool durableStateUpdated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);
        Path = path;
        OperationUsn = operationUsn;
        Snapshot = snapshot;
        DurableStateUpdated = durableStateUpdated;
    }

    /// <summary>Gets the normalized absolute path that was mutated.</summary>
    public string Path { get; }

    /// <summary>Gets the final USN returned by Windows, when the native operation supplies one.</summary>
    public long? OperationUsn { get; }

    /// <summary>Gets a fresh handle-free snapshot captured after the operation.</summary>
    public CloudItemSnapshot Snapshot { get; }

    /// <summary>Gets whether the operation changed and committed durable identity state.</summary>
    public bool DurableStateUpdated { get; }
}

/// <summary>
/// Reports that a file-system mutation completed but its corresponding durable-state change did
/// not commit.
/// </summary>
/// <remarks>
/// Windows and a configured state store cannot share one physical transaction. The exception
/// preserves the completed operation and USN so callers can diagnose and reconcile the split
/// outcome. Retrying identity operations is safe when the native identity already matches.
/// </remarks>
public sealed class CloudItemCoordinationException : Exception
{
    internal CloudItemCoordinationException(
        string operation,
        string path,
        long? operationUsn,
        Exception innerException)
        : base(
            $"File-system operation '{operation}' completed for '{path}', but its durable state could not be committed.",
            innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Operation = operation;
        Path = path;
        OperationUsn = operationUsn;
    }

    /// <summary>Gets the stable CfSharp operation name.</summary>
    public string Operation { get; }

    /// <summary>Gets the normalized absolute path changed by Windows.</summary>
    public string Path { get; }

    /// <summary>Gets the final USN returned by Windows, when available.</summary>
    public long? OperationUsn { get; }
}
