namespace CfSharp;

public abstract partial class CloudItem
{
    /// <summary>Sets this placeholder's explicit pin intent without changing synchronization state.</summary>
    /// <param name="target">Pin, unpin, exclude, clear, or inherit intent.</param>
    /// <param name="cancellationToken">Token observed before the synchronous native call.</param>
    /// <returns>A fresh snapshot after the state change.</returns>
    /// <exception cref="CloudFilesException">Windows rejects the requested pin state.</exception>
    public ValueTask<CloudStateChangeResult> SetPinStateAsync(
        CloudPinTarget target,
        CancellationToken cancellationToken = default) =>
        Owner.SetPinStateAsync(this, target, cancellationToken);

    /// <summary>Sets synchronization state, optionally conditioned on the current USN.</summary>
    /// <param name="inSync">Whether local state agrees with provider state.</param>
    /// <param name="options">Optional positive expected USN, or null for no condition.</param>
    /// <param name="cancellationToken">Token observed before the synchronous native call.</param>
    /// <returns>The Windows-returned USN and a fresh snapshot.</returns>
    /// <exception cref="CloudFilesException">Windows rejects the state or USN condition.</exception>
    public ValueTask<CloudStateChangeResult> SetInSyncAsync(
        bool inSync,
        CloudInSyncChangeOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Owner.SetInSyncAsync(
            this,
            inSync,
            options ?? new CloudInSyncChangeOptions(),
            cancellationToken);

    /// <summary>Converts this existing ordinary item into a Cloud Files placeholder.</summary>
    /// <param name="identity">Stable CfSharp and provider identity stored with the item.</param>
    /// <param name="options">Conversion behavior, or null for content-preserving defaults.</param>
    /// <param name="cancellationToken">
    /// Token observed before the synchronous native call and during durable-state access.
    /// </param>
    /// <returns>A final USN, fresh snapshot, and durable-state outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identity"/> is null.</exception>
    /// <exception cref="ArgumentException">An option does not apply to this item kind.</exception>
    /// <exception cref="CloudFilesException">Windows rejects conversion.</exception>
    /// <exception cref="CloudItemCoordinationException">
    /// Windows converted the item but durable identity state could not be committed.
    /// </exception>
    public ValueTask<CloudPlaceholderMutationResult> ConvertToPlaceholderAsync(
        CloudPlaceholderIdentity identity,
        CloudPlaceholderConversionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Owner.ConvertToPlaceholderAsync(
            this,
            identity,
            options ?? CloudPlaceholderConversionOptions.Default,
            cancellationToken);
    }

    /// <summary>Applies one explicit atomic patch to this existing placeholder.</summary>
    /// <param name="patch">Validated explicit metadata, identity, state, and range changes.</param>
    /// <param name="cancellationToken">
    /// Token observed before the synchronous native call and during durable-state access.
    /// </param>
    /// <returns>A final USN, fresh snapshot, and durable-state outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="patch"/> is null.</exception>
    /// <exception cref="ArgumentException">A patch field does not apply to this item kind.</exception>
    /// <exception cref="CloudFilesException">Windows rejects the update or its USN condition.</exception>
    /// <exception cref="CloudItemCoordinationException">
    /// Windows updated identity but durable state could not be committed.
    /// </exception>
    public ValueTask<CloudPlaceholderMutationResult> UpdatePlaceholderAsync(
        CloudPlaceholderPatch patch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return Owner.UpdatePlaceholderAsync(this, patch, cancellationToken);
    }

    /// <summary>Reverts this placeholder to an ordinary file or directory.</summary>
    /// <param name="cancellationToken">
    /// Token observed before the synchronous native call and during durable-state access. Reverting
    /// an incomplete file may synchronously request content from the connected provider.
    /// </param>
    /// <returns>A fresh ordinary-item snapshot and durable-state removal outcome.</returns>
    /// <exception cref="CloudFilesException">
    /// Windows rejects reversion or required file hydration fails.
    /// </exception>
    /// <exception cref="CloudItemCoordinationException">
    /// Windows reverted the item but durable identity state could not be removed.
    /// </exception>
    public ValueTask<CloudPlaceholderMutationResult> RevertToRegularItemAsync(
        CancellationToken cancellationToken = default) =>
        Owner.RevertToRegularItemAsync(this, cancellationToken);
}
