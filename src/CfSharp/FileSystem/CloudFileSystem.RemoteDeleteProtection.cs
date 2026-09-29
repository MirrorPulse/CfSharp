namespace CfSharp;

public sealed partial class CloudFileSystem
{
    private async ValueTask<bool> CanDeleteRemoteTreeAsync(
        IReadOnlyList<RecursiveItemEntry> entries,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using ICloudStateTransaction transaction = await _stateStore!
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<CloudItemState> states = await transaction.Items
            .ListSubtreeAsync(relativePath, cancellationToken).ConfigureAwait(false);
        foreach (CloudItemState state in states)
        {
            if ((await transaction.Operations.ListByItemIdAsync(state.ItemId, 1, cancellationToken)
                .ConfigureAwait(false)).Count != 0)
            {
                return false;
            }
        }

        foreach (RecursiveItemEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsLink)
            {
                return false;
            }

            LocalCloudItemInspection local = CloudItemInspector.Inspect(entry.FullPath, entry.Kind);
            CloudItemState? state = states.FirstOrDefault(candidate => string.Equals(
                candidate.RelativePath, entry.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (!local.Exists || !local.PlaceholderState.HasFlag(CloudPlaceholderState.Placeholder) ||
                local.SynchronizationState != CloudSynchronizationState.InSync ||
                state is null || state.IsTombstone || state.Kind != entry.Kind ||
                !CloudPlaceholderIdentity.TryDecode(local.PlaceholderIdentity, out CloudPlaceholderIdentity? identity) ||
                identity.ItemId != state.ItemId || identity.RemoteId != state.RemoteId ||
                identity.RemoteRevision != state.RemoteRevision)
            {
                return false;
            }
        }

        return true;
    }
}
