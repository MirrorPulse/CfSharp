namespace CfSharp;

/// <summary>Describes the outcome of one entry in a batch or recursive operation.</summary>
public enum CloudItemOperationStatus
{
    /// <summary>The requested entry operation completed.</summary>
    Succeeded = 0,

    /// <summary>The entry was attempted and failed.</summary>
    Failed = 1,

    /// <summary>The entry was not attempted after an earlier stop condition.</summary>
    NotProcessed = 2,
}

/// <summary>Identifies completed steps of a multi-step placeholder creation entry.</summary>
[Flags]
public enum CloudPlaceholderCreationProgress
{
    /// <summary>No file-system or durable-state change completed.</summary>
    None = 0,

    /// <summary>Windows created the requested placeholder namespace entry.</summary>
    PlaceholderCreated = 1 << 0,

    /// <summary>The placeholder identity mapping was committed to durable state.</summary>
    DurableStatePersisted = 1 << 1,

    /// <summary>The requested file content was hydrated completely.</summary>
    ContentHydrated = 1 << 2,

    /// <summary>The requested pin-state transition completed.</summary>
    PinStateApplied = 1 << 3,

    /// <summary>An existing placeholder with the exact requested identity was reused.</summary>
    ExistingPlaceholderMatched = 1 << 4,
}

/// <summary>Describes one immutable placeholder-creation entry outcome.</summary>
public sealed class CloudPlaceholderBatchEntryResult
{
    internal CloudPlaceholderBatchEntryResult(
        CloudPlaceholderSpec specification,
        string path,
        CloudItemOperationStatus status,
        CloudPlaceholderCreationProgress progress,
        long? createUsn,
        CloudItem? item,
        CloudFilesException? error)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        status = CloudPlaceholderConversionOptions.RequireDefined(status, nameof(status));
        const CloudPlaceholderCreationProgress allProgress =
            CloudPlaceholderCreationProgress.PlaceholderCreated |
            CloudPlaceholderCreationProgress.DurableStatePersisted |
            CloudPlaceholderCreationProgress.ContentHydrated |
            CloudPlaceholderCreationProgress.PinStateApplied |
            CloudPlaceholderCreationProgress.ExistingPlaceholderMatched;
        if ((progress & ~allProgress) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(progress),
                progress,
                "The creation progress contains undefined values.");
        }

        if ((status is CloudItemOperationStatus.Failed) != (error is not null))
        {
            throw new ArgumentException(
                "Exactly failed entries must carry a Cloud Files error.",
                nameof(error));
        }

        bool placeholderCreated = progress.HasFlag(
            CloudPlaceholderCreationProgress.PlaceholderCreated);
        bool existingPlaceholderMatched = progress.HasFlag(
            CloudPlaceholderCreationProgress.ExistingPlaceholderMatched);
        if (placeholderCreated && existingPlaceholderMatched)
        {
            throw new ArgumentException(
                "An entry cannot be both newly created and matched from existing state.",
                nameof(progress));
        }

        if (placeholderCreated != (createUsn is not null))
        {
            throw new ArgumentException(
                "Exactly newly created entries must carry a creation USN.",
                nameof(progress));
        }

        bool placeholderAvailable = placeholderCreated || existingPlaceholderMatched;
        if (placeholderAvailable != (item is not null))
        {
            throw new ArgumentException(
                "Created or matched entries must carry an item reference.",
                nameof(progress));
        }

        CloudPlaceholderCreationProgress laterSteps = progress &
            (CloudPlaceholderCreationProgress.DurableStatePersisted |
             CloudPlaceholderCreationProgress.ContentHydrated |
             CloudPlaceholderCreationProgress.PinStateApplied);
        if (!placeholderAvailable && laterSteps is not CloudPlaceholderCreationProgress.None)
        {
            throw new ArgumentException(
                "Later creation steps cannot complete before the placeholder exists.",
                nameof(progress));
        }

        if (status is CloudItemOperationStatus.Succeeded && !placeholderAvailable)
        {
            throw new ArgumentException(
                "A successful entry must identify the created placeholder.",
                nameof(status));
        }

        if (status is CloudItemOperationStatus.NotProcessed &&
            progress is not CloudPlaceholderCreationProgress.None)
        {
            throw new ArgumentException(
                "An unprocessed entry cannot report completed steps.",
                nameof(progress));
        }

        Specification = specification;
        Path = path;
        Status = status;
        Progress = progress;
        CreateUsn = createUsn;
        Item = item;
        Error = error;
    }

    /// <summary>Gets the immutable input specification.</summary>
    public CloudPlaceholderSpec Specification { get; }

    /// <summary>Gets the normalized absolute target path.</summary>
    public string Path { get; }

    /// <summary>Gets whether the entry succeeded, failed, or was not processed.</summary>
    public CloudItemOperationStatus Status { get; }

    /// <summary>
    /// Gets the completed steps, including partial progress retained when a later step failed.
    /// </summary>
    public CloudPlaceholderCreationProgress Progress { get; }

    /// <summary>Gets the creation USN returned by Windows after success.</summary>
    public long? CreateUsn { get; }

    /// <summary>Gets the created immutable item reference after success.</summary>
    public CloudItem? Item { get; }

    /// <summary>Gets the preserved Windows failure for a failed entry.</summary>
    public CloudFilesException? Error { get; }

    /// <summary>Gets whether corresponding durable identity state was committed.</summary>
    public bool DurableStatePersisted => Progress.HasFlag(
        CloudPlaceholderCreationProgress.DurableStatePersisted);
}

/// <summary>Contains ordered immutable results for one placeholder-creation batch.</summary>
public sealed class CloudPlaceholderBatchResult
{
    private readonly IReadOnlyList<CloudPlaceholderBatchEntryResult> _entries;

    internal CloudPlaceholderBatchResult(
        IEnumerable<CloudPlaceholderBatchEntryResult> entries,
        CloudFilesException? batchError = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = Array.AsReadOnly(entries.ToArray());
        BatchError = batchError;
    }

    /// <summary>Gets results in the same order as the input specifications.</summary>
    public IReadOnlyList<CloudPlaceholderBatchEntryResult> Entries => _entries;

    /// <summary>
    /// Gets the batch-level Windows failure, independently of per-entry failures.
    /// </summary>
    public CloudFilesException? BatchError { get; }

    /// <summary>Gets the number of successful entries.</summary>
    public int SucceededCount => _entries.Count(static entry =>
        entry.Status is CloudItemOperationStatus.Succeeded);

    /// <summary>Gets the number of failed entries.</summary>
    public int FailedCount => _entries.Count(static entry =>
        entry.Status is CloudItemOperationStatus.Failed);

    /// <summary>Gets the number of entries not processed after a stop condition.</summary>
    public int NotProcessedCount => _entries.Count(static entry =>
        entry.Status is CloudItemOperationStatus.NotProcessed);

    /// <summary>Gets whether every requested entry was processed successfully.</summary>
    public bool IsSuccessful =>
        BatchError is null && FailedCount == 0 && NotProcessedCount == 0;

    /// <summary>Throws an aggregate containing every preserved entry failure.</summary>
    /// <exception cref="AggregateException">The batch or at least one entry failed.</exception>
    public void ThrowIfAnyFailed()
    {
        IEnumerable<CloudFilesException> entryFailures = _entries
            .Where(static entry => entry.Error is not null)
            .Select(static entry => entry.Error!);
        CloudFilesException[] failures = BatchError is null
            ? entryFailures.ToArray()
            : entryFailures.Prepend(BatchError).ToArray();
        if (failures.Length != 0)
        {
            throw new AggregateException("One or more placeholder entries failed.", failures);
        }
    }
}

/// <summary>
/// Reports that Windows created or matched placeholders but their identity mappings could not be
/// committed to the configured durable state store.
/// </summary>
/// <remarks>
/// Windows file-system changes and a custom state store cannot share one physical transaction.
/// <see cref="AppliedResult"/> is immutable and identifies the namespace work completed before the
/// store failure. Callers should retain the failure for diagnostics and reconcile or retry the
/// operation; identity-based retry matching prevents duplicate placeholder creation.
/// </remarks>
public sealed class CloudPlaceholderPersistenceException : Exception
{
    internal CloudPlaceholderPersistenceException(
        CloudPlaceholderBatchResult appliedResult,
        Exception innerException)
        : base(
            "Windows applied one or more placeholder entries, but their durable state could not be committed.",
            innerException)
    {
        ArgumentNullException.ThrowIfNull(appliedResult);
        AppliedResult = appliedResult;
    }

    /// <summary>Gets the ordered immutable result of work applied before persistence failed.</summary>
    public CloudPlaceholderBatchResult AppliedResult { get; }
}

/// <summary>Describes one immutable result from explicit recursive execution.</summary>
public sealed class CloudRecursiveOperationEntryResult
{
    internal CloudRecursiveOperationEntryResult(
        string path,
        CloudItemKind kind,
        CloudItemOperationStatus status,
        CloudFilesException? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Kind = CloudPlaceholderConversionOptions.RequireDefined(kind, nameof(kind));
        Status = CloudPlaceholderConversionOptions.RequireDefined(status, nameof(status));
        if ((status is CloudItemOperationStatus.Failed) != (error is not null))
        {
            throw new ArgumentException(
                "Exactly failed entries must carry a Cloud Files error.",
                nameof(error));
        }

        Path = path;
        Error = error;
    }

    /// <summary>Gets the normalized absolute entry path.</summary>
    public string Path { get; }

    /// <summary>Gets whether the entry is a file or directory.</summary>
    public CloudItemKind Kind { get; }

    /// <summary>Gets whether the entry succeeded, failed, or was not processed.</summary>
    public CloudItemOperationStatus Status { get; }

    /// <summary>Gets the preserved Windows failure for a failed entry.</summary>
    public CloudFilesException? Error { get; }
}

/// <summary>Contains ordered immutable results for one explicit recursive operation.</summary>
public sealed class CloudRecursiveOperationResult
{
    private readonly IReadOnlyList<CloudRecursiveOperationEntryResult> _entries;

    internal CloudRecursiveOperationResult(IEnumerable<CloudRecursiveOperationEntryResult> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = Array.AsReadOnly(entries.ToArray());
    }

    /// <summary>Gets entries in deterministic execution order.</summary>
    public IReadOnlyList<CloudRecursiveOperationEntryResult> Entries => _entries;

    /// <summary>Gets whether every entry completed successfully.</summary>
    public bool IsSuccessful => _entries.All(static entry =>
        entry.Status is CloudItemOperationStatus.Succeeded);

    /// <summary>Throws an aggregate containing every preserved entry failure.</summary>
    /// <exception cref="AggregateException">At least one entry failed.</exception>
    public void ThrowIfAnyFailed()
    {
        CloudFilesException[] failures = _entries
            .Where(static entry => entry.Error is not null)
            .Select(static entry => entry.Error!)
            .ToArray();
        if (failures.Length != 0)
        {
            throw new AggregateException("One or more recursive entries failed.", failures);
        }
    }
}
