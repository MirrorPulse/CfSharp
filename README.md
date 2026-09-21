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

## Cloud File-System Lifecycle

`CloudFileSystem` is the normal process-scoped facade. Building validates and freezes its
configuration without acquiring resources. Starting opens the configured durable store, applies
or verifies persistent registration, and connects the optional content provider:

```csharp
await using CloudFileSystem fileSystem = CloudFileSystem.CreateBuilder(localDirectory)
    .WithStateStore(
        new SqliteCloudStateStoreFactory(
            @"C:\ProgramData\ExampleProvider\Accounts\account-42\cfsharp.db"))
    .WithRegistration(registration)
    .WithContentProvider(contentProvider)
    .Build();

await fileSystem.StartAsync(cancellationToken);

CloudFile report = fileSystem.GetFile(@"Documents\report.pdf");
CloudItemSnapshot current = await report.InspectAsync(cancellationToken);
```

Item work admitted by a started facade holds an explicit operation lease. Conflicting path scopes
are serialized while non-overlapping paths may proceed concurrently. Disposal rejects new work,
waits for admitted operations to finish, stops the provider session, and then closes durable state.
It intentionally leaves the persistent sync-root registration installed. Use
`CloudSyncRoot.Unregister()` only for explicit account removal or uninstall.

`CloudFile`, `CloudDirectory`, and the root directory are immutable path references. They keep no
native handle and cache no mutable attributes. Every `InspectAsync()` call returns a fresh,
immutable snapshot combining current local metadata, independent Cloud Files placeholder flags,
content availability, pin and in-sync state, opaque placeholder identity, and any matching durable
item mapping. A missing local item is represented by `Exists == false`, allowing a durable
tombstone to remain visible without inventing file-system state.

Local tree navigation is explicit and side-effect free:

```csharp
CloudDirectory documents = fileSystem.Root.GetDirectory("Documents");
CloudItem existing = documents.Resolve("report.pdf");

CloudDirectoryEnumerationOptions textFiles = CloudDirectoryEnumerationOptions
    .CreateBuilder()
    .WithSearchPattern("*.txt")
    .WithEntryKinds(CloudDirectoryEntryKinds.Files)
    .WithOrder(CloudDirectoryEnumerationOrder.NameAscending)
    .WithRecursion()
    .Build();

await foreach (CloudItem item in documents.EnumerateLocalChildrenAsync(textFiles, cancellationToken))
{
    // Each result is an immutable path reference; inspect it for fresh state.
}
```

Local enumeration never calls the remote content provider or changes the namespace. Recursion is
opt-in, streams results breadth-first, and never follows directory reparse points. Existing links
that resolve outside the sync root are rejected rather than traversed.

## Placeholder Operation Models

Placeholder operations use immutable, kind-specific specifications rather than native structures
or flags. A specification owns a stable CfSharp item identity, provider remote identity and
revision, metadata, collision behavior, and requested initial state:

```csharp
CloudFilePlaceholderSpec report = CloudFilePlaceholderSpec
    .CreateBuilder("report.pdf", "remote-report-42", length: 128_000)
    .WithRemoteRevision("etag-7")
    .WithInitialAvailability(CloudAvailabilityTarget.OnlineOnly)
    .Build();
```

`CloudPlaceholderIdentity` uses a deterministic, versioned envelope suitable for the native
Cloud Files identity blob. It can be encoded or decoded without native resources and is limited to
the platform's 4 KiB maximum. Remote identifiers and revisions must never contain credentials or
secrets. `CloudFileRange`, conversion options, explicit placeholder patches, and batch/recursive
result values preserve validation and partial-failure information without exposing native unions.

Create direct children in one validated batch. Results remain in input order and preserve native
batch failures, per-entry failures, and completed post-creation steps independently:

```csharp
CloudPlaceholderBatchResult result = await fileSystem.Root.CreatePlaceholdersAsync(
    [report, CloudDirectoryPlaceholderSpec.CreateBuilder("Archive", "archive-42").Build()]);

result.ThrowIfAnyFailed();
```

Successful namespace entries are committed to the configured state store in one transaction.
Retrying the same path and encoded identity is idempotent. An unrelated existing item remains a
conflict unless its specification explicitly requests supersede.

Existing items expose explicit conversion, patch, and reversion operations. Patches distinguish
unchanged, replacement, and removal semantics and can condition an update on the observed USN:

```csharp
CloudPlaceholderMutationResult updated = await file.UpdatePlaceholderAsync(
    CloudPlaceholderPatch.CreateBuilder()
        .WithIdentity(newIdentity)
        .WithInSyncVerification()
        .WithExpectedUsn(observedUsn)
        .Build());
```

Mutation results contain a fresh post-operation snapshot and never retain native handles.

Pin intent, synchronization state, and physical content remain independently controllable. The
three availability targets are convenience transitions with explicit partial-failure reporting:

```csharp
CloudAvailabilityChangeResult local = await file.SetAvailabilityAsync(
    CloudAvailabilityTarget.LocallyAvailable,
    cancellationToken);

await file.SetInSyncAsync(inSync: true, cancellationToken: cancellationToken);
await file.DehydrateAsync(new CloudFileRange(0, 64 * 1024), cancellationToken: cancellationToken);

IReadOnlyList<CloudFileRange> onDisk = await file.GetRangesAsync(
    CloudPlaceholderRangeKind.OnDisk,
    CloudFileRange.WholeFile,
    cancellationToken);
```

`OnlineOnly` unpins and then dehydrates the complete file. `LocallyAvailable` and
`AlwaysAvailable` hydrate first, then apply their final unpinned or pinned intent so a pin-triggered
provider request cannot race the explicit hydration. Windows may still be completing an earlier
asynchronous pin notification; in that narrow case CfSharp uses a bounded, cancellation-aware
retry for `ERROR_CLOUD_FILE_UNSUCCESSFUL`. Other native failures are not retried. If either native step fails,
`CloudAvailabilityTransitionException` preserves the original `CloudFilesException`, completed
steps, and a fresh post-failure snapshot. Range results are normalized, ordered, immutable, and
kept separate for on-disk, provider-validated, and locally modified content.

Moves keep path references immutable and update durable directory descendants together after the
file-system move succeeds:

```csharp
CloudItemMoveResult moved = await file.MoveToAsync(
    archiveDirectory,
    "report-final.pdf",
    cancellationToken: cancellationToken);

CloudFile movedFile = (CloudFile)moved.Item;
CloudItemDeleteResult deleted = await movedFile.DeleteAsync(cancellationToken);
```

The original `file` reference remains bound to its old path. Deleting a tracked item commits a
durable tombstone so a later local-change pipeline can publish the deletion. Because Windows and
the configured state store cannot share one physical transaction, a store failure after a move or
delete becomes `CloudItemCoordinationException`; its paths identify the namespace work that already
completed.

Recursive operations are always explicit and operate only on the currently materialized local
tree. They return deterministic per-entry success, failure, or not-processed results:

```csharp
CloudRecursiveOperationResult result = await folder.DeleteTreeAsync(
    new CloudRecursiveOperationOptions(includeRoot: true, stopOnFirstFailure: false),
    cancellationToken);

result.ThrowIfAnyFailed();
```

Recursive state changes visit parents before children; recursive deletion visits children before
parents. Symbolic links and junctions are never traversed. State operations omit link entries,
while deletion removes only the link itself and leaves its target untouched. These methods do not
enumerate remote children or substitute for provider `FETCH_PLACEHOLDERS` callbacks.

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
