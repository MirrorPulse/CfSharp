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

### Restoring a conflict center

After starting the file system, call `ListRemoteConflictsAsync()` to obtain a complete transactional snapshot of unresolved conflicts. Each `CloudRemoteConflictRecord` includes `ConflictId`, `SyncRootPath`, and the decoded `Conflict`. Route subsequent decisions to that root's instance. `GetRemoteConflictAsync(id)` returns null when an unresolved record no longer exists. Queries do not resolve or defer conflicts and do not require applications to interpret storage payloads.

`Conflict.LocalState` represents state at query time, not at the original conflict. Results own their data and can outlive the instance. Listing uses memory proportional to the complete conflict set; corrupt or unsupported envelopes fail visibly instead of disappearing from the UI. Retain an application instance identifier separately when roots can move.
