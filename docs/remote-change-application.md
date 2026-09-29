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

### Recovering newly created remote placeholders

New file and directory upserts prepare a durable creation intent before touching the namespace. The native identity is retained for retries; item state and identity-scoped echo suppression commit together, with foreign keys enabled. Batch progress removes the completed intent in its own atomic outcome transaction. If native creation succeeds but persistence fails, replay the unchanged batch to reconcile the same placeholder rather than generating a new item identity.

The local feed retains observations made during an uncommitted creation separately and reports `RequiresFullRescan` before delivering upload candidates. Replay the remote operation first, then reconcile the root and acknowledge the rescan. A rescan acknowledgement cannot discard a still-pending creation. Ordinary subsequent local edits are not suppressed when native state is out of sync. Native creation and the database are not one atomic resource; failures retain recovery evidence and never advance the batch cursor prematurely.

The official SQLite store upgrades to schema 5 to prevent older versions from ignoring this recovery protocol. Do not downgrade an upgraded database. Custom transactional stores remain supported using existing repositories, but all accessing runtimes must use the new protocol version together. Creation transactions contain a synchronous placeholder creation, not hydration or application callbacks.
