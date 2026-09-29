# Remote change application

Remote transport, authentication, cursors, content bytes, and business conflict policy belong to
the host application. CfSharp applies an immutable batch to the local namespace and records durable
progress around it.

## Submit an immutable batch

```csharp
CloudRemoteChangeBatch batch = new(
    "provider-batch-42",
    initialCursor,
    changes,
    finalCursor);

CloudRemoteApplyResult result = await fileSystem.ApplyRemoteChangesAsync(
    batch,
    new CloudRemoteApplyOptions { ConflictResolver = conflictResolver },
    cancellationToken);
```

Each entry has a durable result. The cursor advances only after the corresponding work is committed.
If a process stops after a partial batch, submitting the same immutable batch resumes at the safe
checkpoint. A retry is expected behavior, not evidence that the provider should create duplicate
namespace entries.

## Conflicts and directories

Conflicts are durable records that the application can resolve explicitly. Directory metadata can
be queried without creating local placeholders; materialized synchronized-directory views are
separate and side-effect free. Neither view performs hidden remote network access.

## Content bytes

Remote file upserts carry metadata and length. A configured content provider hydrates bytes later in
response to Windows demand. Keep authentication tokens, transport cursors, and remote business
payloads in application-owned storage rather than the CfSharp state database.

### Keeping local content and closing a conflict

`KeepLocal` and `Defer` continue to leave the conflict unresolved. To explicitly close an individual persistent conflict without changing local content, call `DismissRemoteConflictAsync(conflictId)`. `Dismissed` means the unresolved record and a durable receipt were atomically updated. A retry, including after restart, returns `AlreadyDismissed`. `NotFound` means no active record or dismissal receipt exists; it is not proof of successful resolution by another action.

Dismissal neither uploads content nor advances a batch cursor, acknowledges pending uploads, or modifies placeholders. New remote changes may conflict again. Receipts are private coordination checkpoints retained without expiry; applications must not delete or parse them. This additive API requires a transactional custom store and works with the official SQLite store. Storage errors are visible and can be retried. Older binaries cannot interpret dismissal receipts, so use a consistent library version for conflict-center actions.
