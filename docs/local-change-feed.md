# Local change feed

The local change feed turns Windows directory notifications into an explicit, bounded application
queue. It is a coordination aid, not a lossless replacement for reconciliation.

## Start one feed per sync root

```csharp
CloudLocalChangeFeed feed = fileSystem.CreateLocalChangeFeed();
await feed.StartAsync(cancellationToken);

CloudLocalChangeBatch batch = await feed.ReadBatchAsync(cancellationToken);
if (batch.RequiresFullRescan)
{
    await ReconcileEntireSyncRootAsync(fileSystem, cancellationToken);
    await feed.AcknowledgeFullRescanAsync(cancellationToken);
}
else
{
    await UploadLocalChangesAsync(batch.Changes, cancellationToken);
    await feed.AcknowledgeAsync(
        batch.Changes.Select(change => change.OperationId),
        cancellationToken);
}
```

The feed normalizes paths relative to the sync root, pairs renames, and persists the ordered
journal with its watcher checkpoint in one transaction. Acknowledgement advances durable progress
only after the application has accepted the batch.

## Rescan is a normal state

A buffer overflow, watcher error, or ambiguous rename is reported as `RequiresFullRescan`. Stop
assuming that notifications are complete, enumerate the materialized local tree, reconcile it, and
acknowledge the rescan explicitly. Recursive operations never follow links or remote children.

## Provider echoes

Provider-originated writes can be wrapped by `SuppressProviderEchoAsync` so they do not become
uploads. Hydration, pinning, and availability transitions are not local upload operations by
themselves.
