namespace CfSharp;

public sealed partial class CloudDirectory
{
    /// <summary>Applies a file availability target across the materialized local tree.</summary>
    /// <param name="target">Availability target for files and corresponding pin intent for directories.</param>
    /// <param name="options">Root inclusion and stop-on-first-failure behavior, or null for defaults.</param>
    /// <param name="cancellationToken">Token observed before each synchronous native call.</param>
    /// <returns>Ordered per-entry results for non-link items visited parent-first.</returns>
    /// <remarks>
    /// The operation neither discovers remote children nor follows symbolic links or junctions.
    /// Synchronous native calls already entered cannot be canceled. Windows failures are retained
    /// in the corresponding entry rather than thrown directly.
    /// </remarks>
    public ValueTask<CloudRecursiveOperationResult> SetAvailabilityRecursivelyAsync(
        CloudAvailabilityTarget target,
        CloudRecursiveOperationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Owner.SetAvailabilityRecursivelyAsync(
            this,
            target,
            options ?? CloudRecursiveOperationOptions.Default,
            cancellationToken);

    /// <summary>Applies explicit pin intent across the materialized local tree.</summary>
    /// <param name="target">Pin, unpin, exclude, clear, or inherit intent.</param>
    /// <param name="options">Root inclusion and stop-on-first-failure behavior, or null for defaults.</param>
    /// <param name="cancellationToken">Token observed before each synchronous native call.</param>
    /// <returns>Ordered per-entry results for non-link items visited parent-first.</returns>
    /// <remarks>
    /// The operation neither discovers remote children nor follows symbolic links or junctions.
    /// Synchronous native calls already entered cannot be canceled. Windows failures are retained
    /// in the corresponding entry rather than thrown directly.
    /// </remarks>
    public ValueTask<CloudRecursiveOperationResult> SetPinStateRecursivelyAsync(
        CloudPinTarget target,
        CloudRecursiveOperationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Owner.SetPinStateRecursivelyAsync(
            this,
            target,
            options ?? CloudRecursiveOperationOptions.Default,
            cancellationToken);

    /// <summary>Deletes the materialized local tree without following symbolic links or junctions.</summary>
    /// <param name="options">Root inclusion and stop-on-first-failure behavior, or null for defaults.</param>
    /// <param name="cancellationToken">
    /// Token observed between entries and during tombstone persistence. Earlier entries may have
    /// completed when cancellation is observed.
    /// </param>
    /// <returns>Ordered per-entry results in children-before-parent deletion order.</returns>
    /// <exception cref="InvalidOperationException">
    /// The sync root was selected while root deletion was enabled.
    /// </exception>
    /// <exception cref="CloudItemCoordinationException">
    /// An entry was deleted but its durable tombstone could not be committed. The exception's
    /// <see cref="CloudItemCoordinationException.PartialResult"/> preserves prior results.
    /// </exception>
    /// <exception cref="CloudRecursiveOperationCanceledException">
    /// Cancellation was observed after one or more entries completed; the exception preserves the
    /// partial result.
    /// </exception>
    /// <remarks>
    /// Windows deletion failures are retained in the corresponding entry. A symbolic link or
    /// junction is deleted as one entry without visiting or modifying its target.
    /// </remarks>
    public ValueTask<CloudRecursiveOperationResult> DeleteTreeAsync(
        CloudRecursiveOperationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Owner.DeleteTreeAsync(
            this,
            options ?? CloudRecursiveOperationOptions.Default,
            cancellationToken);
}
