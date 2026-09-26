# Placeholders and hydration

Placeholder operations use immutable, kind-specific specifications rather than exposing native
unions and flag combinations directly to application code.

## Create a placeholder

```csharp
CloudFilePlaceholderSpec report = CloudFilePlaceholderSpec
    .CreateBuilder("report.pdf", "remote-report-42", length: 128_000)
    .WithRemoteRevision("etag-7")
    .WithInitialAvailability(CloudAvailabilityTarget.OnlineOnly)
    .Build();

CloudPlaceholderBatchResult result =
    await fileSystem.Root.CreatePlaceholdersAsync([report], cancellationToken);
result.ThrowIfAnyFailed();
```

The identity envelope is versioned, deterministic, and bounded by the native identity limit. Do not
put credentials or secrets in remote identifiers, revisions, or placeholder metadata.

## Availability is explicit

`OnlineOnly`, `LocallyAvailable`, and `AlwaysAvailable` describe different pin and content states.
CfSharp hydrates before applying the final pin intent when necessary and returns partial-failure
information if a transition cannot complete. A native failure remains available through the managed
exception and the post-failure snapshot.

```csharp
CloudAvailabilityChangeResult transition = await file.SetAvailabilityAsync(
    CloudAvailabilityTarget.LocallyAvailable,
    cancellationToken);

await file.SetInSyncAsync(true, cancellationToken);
```

## Ranges and reversion

Range results are normalized and kept separate for on-disk, provider-validated, and locally modified
content. Placeholder patches can condition a change on the observed USN. Move and delete operations
keep immutable path references and update durable descendants only after the file-system operation
succeeds.

## Provider responsibility

CfSharp coordinates the Windows demand callback and durable state; the application supplies content
bytes and remote transport. A provider must be prepared for retries, cancellation, and a callback
that is replayed after a process restart.
