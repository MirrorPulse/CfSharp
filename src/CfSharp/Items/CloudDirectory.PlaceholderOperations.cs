namespace CfSharp;

public sealed partial class CloudDirectory
{
    /// <summary>Marks this placeholder directory as partial or fully populated.</summary>
    /// <param name="state">Requested population state.</param>
    /// <param name="cancellationToken">Token observed before the synchronous native update.</param>
    /// <returns>The completed placeholder mutation.</returns>
    /// <exception cref="CloudFilesException">Windows rejects the population-state update.</exception>
    public ValueTask<CloudPlaceholderMutationResult> SetPopulationStateAsync(
        CloudDirectoryPopulationState state,
        CancellationToken cancellationToken = default) =>
        UpdatePlaceholderAsync(
            CloudPlaceholderPatch.CreateBuilder().WithPopulationState(state).Build(),
            cancellationToken);

    /// <summary>Creates one child placeholder and returns its completed entry result.</summary>
    /// <param name="placeholder">Immutable file or directory placeholder specification.</param>
    /// <param name="cancellationToken">
    /// Token observed before native work and between post-creation steps. A native call already
    /// entered cannot be interrupted, so cancellation may occur after Windows created the item.
    /// </param>
    /// <returns>
    /// A successful immutable result containing the created or identity-matched item reference.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="placeholder"/> is null.</exception>
    /// <exception cref="CloudFilesException">
    /// Windows rejects creation, pinning, or hydration of the placeholder.
    /// </exception>
    /// <exception cref="CloudPlaceholderPersistenceException">
    /// Windows applied the placeholder but its identity mapping could not be committed.
    /// </exception>
    /// <exception cref="OperationCanceledException">The operation was canceled at a safe boundary.</exception>
    public async ValueTask<CloudPlaceholderBatchEntryResult> CreatePlaceholderAsync(
        CloudPlaceholderSpec placeholder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        CloudPlaceholderBatchResult result = await CreatePlaceholdersAsync(
            [placeholder],
            new CloudPlaceholderBatchOptions(stopOnFirstFailure: true),
            cancellationToken).ConfigureAwait(false);
        CloudPlaceholderBatchEntryResult entry = result.Entries[0];
        if (entry.Error is not null)
        {
            throw entry.Error;
        }

        if (result.BatchError is not null)
        {
            throw result.BatchError;
        }

        if (entry.Status is not CloudItemOperationStatus.Succeeded)
        {
            throw new InvalidOperationException("Windows did not process the placeholder entry.");
        }

        return entry;
    }

    /// <summary>Creates a validated batch of direct child placeholders.</summary>
    /// <param name="placeholders">
    /// File and directory specifications. The sequence is materialized once before mutation;
    /// names must be unique under ordinal case-insensitive comparison.
    /// </param>
    /// <param name="options">
    /// Batch behavior, or null for continuing native creation after per-entry failures.
    /// </param>
    /// <param name="cancellationToken">
    /// Token observed before native work and between post-creation steps. Synchronous Cloud Files
    /// calls cannot be interrupted after entry.
    /// </param>
    /// <returns>
    /// Ordered per-entry results. Native batch and entry failures are retained independently, and
    /// a later hydration failure retains completed namespace, durability, and pin steps.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The sequence is empty, contains a null entry, or repeats a child name, item identifier, or
    /// remote identifier.
    /// </exception>
    /// <exception cref="CloudPlaceholderPersistenceException">
    /// Windows created or matched at least one entry but durable state commit failed.
    /// </exception>
    /// <exception cref="OperationCanceledException">The operation was canceled at a safe boundary.</exception>
    /// <remarks>
    /// Existing placeholders with the exact encoded CfSharp identity are idempotent matches.
    /// Unrelated existing items fail unless their specification explicitly requests supersede.
    /// All successful namespace entries are persisted in one state-store transaction before any
    /// requested hydration. The method owns no handle after completion and is safe for concurrent
    /// use on non-overlapping paths while the owning file system remains started.
    /// </remarks>
    public ValueTask<CloudPlaceholderBatchResult> CreatePlaceholdersAsync(
        IEnumerable<CloudPlaceholderSpec> placeholders,
        CloudPlaceholderBatchOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Owner.CreatePlaceholdersAsync(
            this,
            placeholders,
            options ?? CloudPlaceholderBatchOptions.Default,
            cancellationToken);
}
