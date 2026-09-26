# CfSharp Sample

The sample is intentionally small: it mirrors a local content directory into a registered sync
root as online-only placeholders and demonstrates hydration through an ordinary file read. It is
an integration example, not a production remote adapter.

## Run once

```powershell
dotnet run --project samples/CfSharp.SampleProvider -- `
  run C:\CloudContent `
  C:\CloudSyncRoot `
  --state-db C:\CloudState\cfsharp.db `
  --once
```

The host owns registration and unregistration decisions. The sample closes its process-scoped
provider session before exit but intentionally leaves the persistent registration installed. Remove
that registration only when removing the sample account.

## What to inspect

1. The source directory remains outside the sync root.
2. The SQLite database is outside both content trees.
3. Explorer shows online-only placeholders in the registered root.
4. Reading a placeholder requests content from the sample provider.
5. Restarting the sample reuses the same durable state instead of inventing a new checkpoint.

## Limitations

The sample does not provide authentication, a cloud endpoint, conflict policy, production logging,
or a stable release promise. Use it to understand lifecycle and callback wiring, then supply those
responsibilities in the host application.
