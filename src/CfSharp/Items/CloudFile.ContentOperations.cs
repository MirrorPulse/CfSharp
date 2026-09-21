namespace CfSharp;

public sealed partial class CloudFile
{
    /// <summary>Moves this placeholder file to one of three user-facing availability targets.</summary>
    /// <param name="target">Online-only, locally available, or always available.</param>
    /// <param name="cancellationToken">Token observed between pin and content steps.</param>
    /// <returns>Completed substeps and a fresh final snapshot.</returns>
    /// <exception cref="CloudAvailabilityTransitionException">
    /// A native step failed. The exception retains its <see cref="CloudFilesException"/> and a
    /// snapshot describing any earlier step that completed.
    /// </exception>
    public ValueTask<CloudAvailabilityChangeResult> SetAvailabilityAsync(
        CloudAvailabilityTarget target,
        CancellationToken cancellationToken = default) =>
        Owner.SetAvailabilityAsync(this, target, cancellationToken);

    /// <summary>Ensures that a file range is present locally.</summary>
    /// <param name="range">Finite range or range extending through end of file.</param>
    /// <param name="cancellationToken">Token observed before the synchronous native call.</param>
    /// <returns>An operation that completes after Windows hydrates the requested bytes.</returns>
    /// <exception cref="CloudFilesException">Windows or the connected provider rejects hydration.</exception>
    public ValueTask HydrateAsync(
        CloudFileRange range,
        CancellationToken cancellationToken = default) =>
        Owner.HydrateAsync(this, range, cancellationToken);

    /// <summary>Removes locally present content from a file range.</summary>
    /// <param name="range">Finite range or range extending through end of file.</param>
    /// <param name="options">Foreground or background classification, or null for foreground.</param>
    /// <param name="cancellationToken">Token observed before the synchronous native call.</param>
    /// <returns>An operation that completes after Windows dehydrates the requested bytes.</returns>
    /// <exception cref="CloudFilesException">
    /// Windows rejects dehydration, including for pinned, out-of-sync, or always-full files.
    /// </exception>
    public ValueTask DehydrateAsync(
        CloudFileRange range,
        CloudDehydrationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Owner.DehydrateAsync(
            this,
            range,
            options ?? CloudDehydrationOptions.Foreground,
            cancellationToken);

    /// <summary>Returns normalized immutable placeholder ranges in the requested interval.</summary>
    /// <param name="kind">On-disk, provider-validated, or locally modified data.</param>
    /// <param name="range">Finite query interval or interval extending through end of file.</param>
    /// <param name="cancellationToken">Token observed before each synchronous query attempt.</param>
    /// <returns>Ordered non-overlapping ranges. The returned collection is caller-owned.</returns>
    /// <exception cref="CloudFilesException">Windows rejects the range query.</exception>
    public ValueTask<IReadOnlyList<CloudFileRange>> GetRangesAsync(
        CloudPlaceholderRangeKind kind,
        CloudFileRange range,
        CancellationToken cancellationToken = default) =>
        Owner.GetRangesAsync(this, kind, range, cancellationToken);
}
