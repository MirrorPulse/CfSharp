# SQLite state store

`CfSharp.Storage.Sqlite` is the official durable implementation of the core state contracts. The
application supplies its absolute path and owns its backup and retention policy.

## Choose a safe location

The database must be outside the managed sync root and must not contain file content, credentials,
or provider-specific business data. A per-account location under a protected application-data
directory is a practical default:

```text
C:\ProgramData\ExampleProvider\Accounts\account-42\cfsharp.db
```

One database is bound to one sync root and one active CfSharp owner. Do not open the same database
from two unrelated provider sessions.

## Transaction semantics

The provider uses transactions, foreign keys, WAL mode, bounded lock waits, and durable checkpoints.
Dispose an uncommitted transaction to roll back. Windows namespace operations and SQLite transactions
cannot be one physical transaction; if the namespace succeeds and the store fails, the managed
exception identifies the coordination boundary so the application can reconcile.

## Backup and recovery

Treat the database as coordination state that must be backed up according to the account's policy.
After a crash, reopen the same path and let durable checkpoints, tombstones, and journal state drive
replay. Do not repair the file by deleting the WAL or by replacing the database with an empty file.

## Custom stores

Applications that need another database can implement `ICloudStateStoreFactory`,
`ICloudStateStore`, and `ICloudStateTransaction`. Preserve the same commit, rollback, ownership,
checkpoint, and retry semantics; the high-level API does not require SQLite.

### Schema 5 recovery fence

Schema 5 preserves the existing table layout and data while fencing the remote placeholder creation and observation-reconciliation protocol. Versions 0 through 4 upgrade in place. Older libraries reject the newer version rather than ignore pending creation records. Do not downgrade the schema number manually. Back up state before upgrading and use matching core and SQLite packages.
