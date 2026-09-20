# CfSharp

CfSharp is a Windows-only .NET library for the Windows Cloud Files API (`cfapi.h` and `CldApi.dll`).

The project currently contains three packages:

- `CfSharp.Native` provides complete, ABI-accurate native bindings.
- `CfSharp` provides a safe, idiomatic file-system API for sync providers.
- `CfSharp.Storage.Sqlite` provides the optional official durable-state implementation.

The high-level package defines `ICloudStateStoreFactory`, `ICloudStateStore`, and
`ICloudStateTransaction` so custom backends can participate without taking a SQLite dependency.
Transactions expose focused repositories for item mappings, checkpoints, local operations,
conflicts, remote-batch progress, and echo suppression. Disposal without commit rolls back.

The SQLite provider requires an explicit absolute database path outside the managed sync root:

```csharp
ICloudStateStoreFactory stateStoreFactory = new SqliteCloudStateStoreFactory(
    @"C:\ProgramData\ExampleProvider\Accounts\account-42\cfsharp.db");

CloudStateStoreContext context = new(@"C:\Users\Example\Example Cloud");
await using ICloudStateStore store = await stateStoreFactory.OpenAsync(context);
```

One database is bound to one sync root and has one active CfSharp owner. The provider uses
transactions, foreign keys, WAL mode, and a bounded lock wait. Applications may replace it with
any implementation of the core state interfaces that satisfies the same transactional contract.
The state database stores synchronization coordination metadata, never file content or secrets.

## Goals

- Preserve access to every native CFAPI capability.
- Provide an elegant high-level API for files, directories, placeholders, hydration, population, and synchronization state.
- Distinguish Windows demand requests, durable local changes, and application-supplied remote changes.
- Make ownership, cancellation, partial failure, platform requirements, and native errors explicit.
- Maintain detailed public documentation and verifiable ABI compatibility.

## Status

CfSharp is under active development. Platform discovery, persistent sync-root lifecycle, and
native callback, placeholder creation, and transfer primitives are implemented. A safe managed
provider session can hydrate file content on demand. The transactional state contracts and the
official SQLite provider are implemented. Namespace callbacks and the complete file-system
facade are not yet ready. No production package has been released.

## Sync Root Lifecycle

```csharp
SyncRootRegistrationOptions registration =
    SyncRootRegistrationOptions.CreateBuilder("Example Cloud", "1.0.0")
        .WithProviderId(providerId)
        .WithSyncRootIdentity(accountIdentity)
        .WithHydrationPolicy(
            CloudHydrationPolicy.Progressive,
            CloudHydrationPolicyModifiers.AutoDehydrationAllowed)
        .WithPopulationPolicy(CloudPopulationPolicy.Partial)
        .WithRootMarkedInSync()
        .Build();

CloudSyncRoot root = CloudSyncRoot.Register(localDirectory, registration);
CloudSyncRootInfo current = root.GetInfo();
```

Registration is persistent and does not end when the process exits. `Unregister()` is an
explicit account-removal or uninstall operation: Windows traverses the tree and may delete
placeholder content that is not locally complete. It must not be used as routine session cleanup.

## Sample Provider

The sample mirrors files from a local content directory into a registered sync root as
online-only placeholders, then verifies hydration through ordinary file reads:

```powershell
dotnet run --project samples/CfSharp.SampleProvider -- `
  C:\CloudContent `
  C:\CloudSyncRoot
```

The sample closes its process-scoped provider session before exiting but intentionally leaves the
persistent registration installed. Remove it explicitly only when removing that sample account.

## Platform

CfSharp targets Windows and is intended for desktop sync-provider applications.

## Development

The repository currently uses the .NET SDK selected by `global.json`.

```powershell
dotnet restore CfSharp.sln
dotnet build CfSharp.sln --configuration Release --no-restore
dotnet test CfSharp.sln --configuration Release --no-build
dotnet pack src/CfSharp.Native/CfSharp.Native.csproj --configuration Release --no-build
dotnet pack src/CfSharp/CfSharp.csproj --configuration Release --no-build
dotnet pack src/CfSharp.Storage.Sqlite/CfSharp.Storage.Sqlite.csproj --configuration Release --no-build
```

The native ABI probe under `tests/CfSharp.Native.AbiProbe` additionally requires the Microsoft Visual C++ Build Tools.

## Author

MirrorPulse Team

## License

CfSharp is licensed under the [Apache License 2.0](LICENSE).
