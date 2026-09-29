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

### Preserving local descendants during remote directory deletion

The default preservation policy checks the materialized subtree, including ordinary files absent from the state store. An untracked or dirty descendant, a pending descendant operation, a link, or an identity mismatch produces a delete conflict before deletion starts. Parent in-sync state does not certify its descendants. Only preflighted entries are deleted, with a further check before each deletion; newly created children are never swept into a fresh recursive enumeration. Concurrent namespace changes can stop a partially completed deletion, whose durable tombstones are retained. This is not a filesystem-wide transaction. Explicitly disabling preservation retains the existing destructive policy.
