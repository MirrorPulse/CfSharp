# Architecture and ownership

CfSharp uses a one-way dependency graph so that safe application code does not need to understand
native pointers or SQLite implementation details.

```text
CfSharp.Storage.Sqlite  ──depends on──>  CfSharp  ──depends on──>  CfSharp.Native
```

## Native layer

`CfSharp.Native` preserves Cloud Files structures, constants, enums, callbacks, and exports. It is
the escape hatch for complete native coverage and the foundation for ABI probes. Unsafe code,
marshalling rules, structure sizes, and native error values stay here whenever possible.

## High-level layer

`CfSharp` provides immutable path references, validated operation specifications, capability gates,
explicit cancellation, and managed exceptions that retain native failure details. `CloudFileSystem`
is the normal process-scoped facade. It acquires the durable store only after validating the sync
root and takes disposal ownership after a successful start.

## Durable state

State interfaces describe transactions and focused repositories for item mappings, checkpoints,
local operations, conflicts, remote progress, and echo suppression. A transaction that is disposed
without commit rolls back. The application chooses the database path and remains responsible for
backup, retention, and account-level policy.

## Application-owned responsibilities

CfSharp does not decide how to authenticate, fetch remote bytes, resolve business conflicts, or
schedule an account's network work. A content provider supplies those behaviors. Remote batches and
local change acknowledgements are explicit so an application can make retry and recovery decisions
without hidden network access.
