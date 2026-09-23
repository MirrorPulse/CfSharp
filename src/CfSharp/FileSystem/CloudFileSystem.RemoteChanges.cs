using System.Text;

namespace CfSharp;

public sealed partial class CloudFileSystem
{
    private const string RemoteConflictPayloadPrefix = "cfsharp.remote-conflict/v1|";

    /// <summary>
    /// Applies an immutable application-supplied remote change batch to this sync root.
    /// </summary>
    /// <param name="batch">Ordered remote changes and their opaque cursor envelope.</param>
    /// <param name="options">Apply policy, or the conservative defaults.</param>
    /// <param name="cancellationToken">
    /// Token observed between durable entries and before native namespace mutations. A native call
    /// that has already started cannot be interrupted.
    /// </param>
    /// <returns>
    /// Per-entry outcomes and the cursor after the highest durably completed entry. Entries after
    /// a failure or the per-call limit are reported as not processed.
    /// </returns>
    /// <exception cref="ArgumentNullException">The batch is null.</exception>
    /// <exception cref="ArgumentException">The batch conflicts with existing durable progress.</exception>
    /// <exception cref="InvalidOperationException">The file system has not started.</exception>
    /// <exception cref="CloudFilesException">Windows rejects a requested namespace mutation.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was observed at a safe boundary.</exception>
    /// <remarks>
    /// Remote content bytes never enter this method. File upserts update placeholder length,
    /// metadata, identity, and revision while leaving hydration to the configured provider. A
    /// durable batch record is written after each entry, so a process restart resumes from the
    /// last committed entry without publishing an unsafe cursor.
    /// </remarks>
    public async ValueTask<CloudRemoteApplyResult> ApplyRemoteChangesAsync(
        CloudRemoteChangeBatch batch,
        CloudRemoteApplyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        CloudRemoteApplyOptions selectedOptions = options ?? CloudRemoteApplyOptions.Default;
        selectedOptions.Validate();
        EnsureStarted();

        CloudRemoteBatchState progress = await ReadOrInitializeRemoteBatchAsync(
                batch,
                cancellationToken)
            .ConfigureAwait(false);
        List<CloudRemoteApplyEntryResult> results = [];
        List<Guid> conflictIds = [];
        for (int index = 0; index < progress.AppliedEntryCount; index++)
        {
            results.Add(new CloudRemoteApplyEntryResult(
                batch.Changes[index].ChangeId,
                CloudRemoteApplyEntryStatus.AlreadyApplied));
        }

        if (progress.Status is CloudRemoteBatchStatus.Applied)
        {
            for (int index = progress.AppliedEntryCount; index < batch.Changes.Count; index++)
            {
                results.Add(new CloudRemoteApplyEntryResult(
                    batch.Changes[index].ChangeId,
                    CloudRemoteApplyEntryStatus.AlreadyApplied));
            }

            return CreateRemoteApplyResult(progress, results, requiresRetry: false, conflictIds);
        }

        int attempted = 0;
        while (progress.AppliedEntryCount < batch.Changes.Count &&
               attempted < selectedOptions.MaximumEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = progress.AppliedEntryCount;
            CloudRemoteChange change = batch.Changes[index];
            RemoteEntryOutcome outcome;
            try
            {
                outcome = await ApplyRemoteEntryAsync(
                        change,
                        selectedOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                progress = await PersistRemoteBatchFailureAsync(
                        batch,
                        progress,
                        exception,
                        cancellationToken)
                    .ConfigureAwait(false);
                results.Add(new CloudRemoteApplyEntryResult(
                    change.ChangeId,
                    CloudRemoteApplyEntryStatus.Failed,
                    error: exception));
                for (int remaining = index + 1; remaining < batch.Changes.Count; remaining++)
                {
                    results.Add(new CloudRemoteApplyEntryResult(
                        batch.Changes[remaining].ChangeId,
                        CloudRemoteApplyEntryStatus.NotProcessed));
                }

                return CreateRemoteApplyResult(progress, results, requiresRetry: true, conflictIds);
            }

            Guid? conflictId = null;
            if (outcome.Conflict is not null)
            {
                conflictId = Guid.NewGuid();
                conflictIds.Add(conflictId.Value);
            }

            ReadOnlyMemory<byte> cursor = change.CursorAfter.IsEmpty
                ? progress.Cursor
                : change.CursorAfter;
            CloudRemoteBatchStatus status = index == batch.Changes.Count - 1
                ? CloudRemoteBatchStatus.Applied
                : CloudRemoteBatchStatus.Applying;
            if (status is CloudRemoteBatchStatus.Applied && change.CursorAfter.IsEmpty)
            {
                cursor = batch.FinalCursor;
            }

            progress = await PersistRemoteBatchOutcomeAsync(
                    batch,
                    progress,
                    index + 1,
                    change,
                    outcome,
                    conflictId,
                    cursor,
                    status,
                    cancellationToken)
                .ConfigureAwait(false);
            results.Add(new CloudRemoteApplyEntryResult(
                change.ChangeId,
                outcome.Status,
                outcome.Conflict));
            attempted++;
        }

        bool requiresRetry = progress.Status is not CloudRemoteBatchStatus.Applied;
        if (requiresRetry)
        {
            for (int remaining = progress.AppliedEntryCount; remaining < batch.Changes.Count; remaining++)
            {
                results.Add(new CloudRemoteApplyEntryResult(
                    batch.Changes[remaining].ChangeId,
                    CloudRemoteApplyEntryStatus.NotProcessed));
            }
        }

        return CreateRemoteApplyResult(progress, results, requiresRetry, conflictIds);
    }

    private async ValueTask<CloudRemoteBatchState> ReadOrInitializeRemoteBatchAsync(
        CloudRemoteChangeBatch batch,
        CancellationToken cancellationToken)
    {
        await using ICloudStateTransaction transaction = await (_stateStore ?? throw new InvalidOperationException(
                "The cloud file system has no open state store.")).BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        CloudRemoteBatchState? existing = await transaction.RemoteBatches
            .GetAsync(batch.BatchId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.TotalEntryCount != batch.Changes.Count ||
                (!existing.Fingerprint.IsEmpty &&
                 !existing.Fingerprint.Span.SequenceEqual(batch.Fingerprint.Span)))
            {
                throw new ArgumentException(
                    $"Remote batch '{batch.BatchId}' does not match its durable fingerprint.",
                    nameof(batch));
            }

            if (existing.AppliedEntryCount > batch.Changes.Count)
            {
                throw new InvalidOperationException(
                    $"Remote batch '{batch.BatchId}' has invalid durable progress.");
            }

            CloudRemoteBatchStatus status = existing.Status is CloudRemoteBatchStatus.Applied
                ? CloudRemoteBatchStatus.Applied
                : CloudRemoteBatchStatus.Applying;
            CloudRemoteBatchState normalized = new(
                existing.BatchId,
                existing.Cursor.Span,
                existing.AppliedEntryCount,
                existing.TotalEntryCount,
                status,
                existing.Payload.Span,
                DateTimeOffset.UtcNow,
                batch.Fingerprint,
                existing.LastAppliedChangeId);
            if (existing.Status is not CloudRemoteBatchStatus.Applied ||
                existing.Fingerprint.IsEmpty)
            {
                await transaction.RemoteBatches.UpsertAsync(normalized, cancellationToken)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            return normalized;
        }

        CloudRemoteBatchStatus initialStatus = batch.Changes.Count == 0
            ? CloudRemoteBatchStatus.Applied
            : CloudRemoteBatchStatus.Applying;
        CloudRemoteBatchState initial = new(
            batch.BatchId,
            (initialStatus is CloudRemoteBatchStatus.Applied
                ? batch.FinalCursor
                : batch.InitialCursor).Span,
            0,
            batch.Changes.Count,
            initialStatus,
            [],
            DateTimeOffset.UtcNow,
            batch.Fingerprint);
        await transaction.RemoteBatches.UpsertAsync(initial, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return initial;
    }

    private async ValueTask<RemoteEntryOutcome> ApplyRemoteEntryAsync(
        CloudRemoteChange change,
        CloudRemoteApplyOptions options,
        CancellationToken cancellationToken)
    {
        RemoteEntryContext context = await ReadRemoteEntryContextAsync(change, cancellationToken)
            .ConfigureAwait(false);
        bool pathBelongsToAnotherItem = context.ByPath is not null &&
            !context.ByPath.IsTombstone &&
            (context.ByRemoteId is null || context.ByRemoteId.ItemId != context.ByPath.ItemId) &&
            (change.ItemId is null || context.ByItemId is null ||
             context.ByItemId.ItemId != context.ByPath.ItemId);
        if (pathBelongsToAnotherItem ||
            (context.ByPath is not null &&
             context.LocalState is not null &&
             context.ByPath.ItemId != context.LocalState.ItemId &&
             !context.ByPath.IsTombstone))
        {
            return RemoteEntryOutcome.ConflictResult(CreateConflict(
                change,
                context.LocalState,
                CloudRemoteConflictReason.PathCollision));
        }

        if (change.PreviousRemoteRevision is not null &&
            (context.LocalState is null || !string.Equals(
                context.LocalState.RemoteRevision,
                change.PreviousRemoteRevision,
                StringComparison.Ordinal)))
        {
            return RemoteEntryOutcome.ConflictResult(CreateConflict(
                change,
                context.LocalState,
                CloudRemoteConflictReason.StaleRemoteRevision));
        }

        bool upsert = change.Kind is CloudRemoteChangeKind.FileUpsert or
            CloudRemoteChangeKind.DirectoryUpsert;
        if (change.Kind is not CloudRemoteChangeKind.MetadataUpdate && !upsert)
        {
            throw new NotSupportedException(
                $"Remote change kind '{change.Kind}' is implemented by a later Phase 8 increment.");
        }

        if (context.LocalState is not null && context.LocalOperations.Count != 0)
        {
            return RemoteEntryOutcome.ConflictResult(CreateConflict(
                change,
                context.LocalState,
                change.Kind is CloudRemoteChangeKind.MetadataUpdate
                    ? CloudRemoteConflictReason.Metadata
                    : CloudRemoteConflictReason.Content));
        }

        if (!upsert && context.LocalState is null)
        {
            return RemoteEntryOutcome.ConflictResult(CreateConflict(
                change,
                localState: null,
                CloudRemoteConflictReason.MissingItem));
        }

        if (context.LocalState is not null &&
            !string.Equals(
                context.LocalState.RelativePath,
                change.RelativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return RemoteEntryOutcome.ConflictResult(CreateConflict(
                change,
                context.LocalState,
                CloudRemoteConflictReason.Move));
        }

        Guid itemId = context.LocalState?.ItemId ?? change.ItemId ?? Guid.NewGuid();
        bool localExists = change.ItemKind is CloudItemKind.Directory
            ? Directory.Exists(ToFullPath(change.RelativePath))
            : File.Exists(ToFullPath(change.RelativePath));
        if (context.LocalState is not null && localExists)
        {
            CloudItem item = CreateItemReference(change.RelativePath, change.ItemKind);
            CloudItemSnapshot snapshot = await item.InspectAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!snapshot.IsPlaceholder)
            {
                return RemoteEntryOutcome.ConflictResult(CreateConflict(
                    change,
                    context.LocalState,
                    CloudRemoteConflictReason.Content));
            }

            if (options.PreserveUnsynchronizedLocalContent &&
                snapshot.SynchronizationState is CloudSynchronizationState.NotInSync)
            {
                return RemoteEntryOutcome.ConflictResult(CreateConflict(
                    change,
                    context.LocalState,
                    change.Kind is CloudRemoteChangeKind.MetadataUpdate
                        ? CloudRemoteConflictReason.Metadata
                        : CloudRemoteConflictReason.Content));
            }
        }

        CloudStateOperationKind suppressionKind = context.LocalState is null || !localExists
            ? CloudStateOperationKind.Create
            : change.Kind is CloudRemoteChangeKind.MetadataUpdate
                ? CloudStateOperationKind.MetadataUpdate
                : CloudStateOperationKind.ContentUpdate;
        if (options.SuppressLocalEcho)
        {
            await RegisterRemoteEchoSuppressionAsync(
                    change,
                    itemId,
                    suppressionKind,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (context.LocalState is null || !localExists)
        {
            if (!upsert)
            {
                return RemoteEntryOutcome.ConflictResult(CreateConflict(
                    change,
                    context.LocalState,
                    CloudRemoteConflictReason.MissingItem));
            }

            await CreateRemotePlaceholderAsync(change, itemId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await UpdateRemotePlaceholderAsync(change, context.LocalState!, cancellationToken)
                .ConfigureAwait(false);
        }

        return RemoteEntryOutcome.AppliedResult;
    }

    private async ValueTask<RemoteEntryContext> ReadRemoteEntryContextAsync(
        CloudRemoteChange change,
        CancellationToken cancellationToken)
    {
        await using ICloudStateTransaction transaction = await (_stateStore ?? throw new InvalidOperationException(
                "The cloud file system has no open state store.")).BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        CloudItemState? byRemoteId = await transaction.Items
            .GetByRemoteIdAsync(change.RemoteId, cancellationToken)
            .ConfigureAwait(false);
        CloudItemState? byItemId = change.ItemId is Guid itemId
            ? await transaction.Items.GetByItemIdAsync(itemId, cancellationToken).ConfigureAwait(false)
            : null;
        CloudItemState? byPath = await transaction.Items
            .GetByRelativePathAsync(change.RelativePath, cancellationToken)
            .ConfigureAwait(false);
        CloudItemState? localState = byRemoteId ?? byItemId ?? byPath;
        IReadOnlyList<CloudOperationJournalEntry> operations = localState is null
            ? []
            : (await transaction.Operations.ListAsync(4096, cancellationToken).ConfigureAwait(false))
                .Where(operation => operation.ItemId == localState.ItemId)
                .ToArray();
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        return new RemoteEntryContext(localState, byRemoteId, byItemId, byPath, operations);
    }

    private async ValueTask RegisterRemoteEchoSuppressionAsync(
        CloudRemoteChange change,
        Guid itemId,
        CloudStateOperationKind kind,
        CloudRemoteApplyOptions options,
        CancellationToken cancellationToken)
    {
        await using ICloudStateTransaction transaction = await (_stateStore ?? throw new InvalidOperationException(
                "The cloud file system has no open state store.")).BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await transaction.EchoSuppressions.UpsertAsync(
            new CloudEchoSuppressionState(
                Guid.NewGuid(),
                itemId,
                kind,
                change.RelativePath,
                Encoding.UTF8.GetBytes($"remote-change/v1|{change.ChangeId}"),
                DateTimeOffset.UtcNow + options.EchoSuppressionLifetime,
                change.PreviousRelativePath,
                options.EchoSuppressionObservationCount),
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask CreateRemotePlaceholderAsync(
        CloudRemoteChange change,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        CloudPlaceholderIdentity identity = new(itemId, change.RemoteId, change.RemoteRevision);
        string parentPath = Path.GetDirectoryName(change.RelativePath) ?? string.Empty;
        string name = Path.GetFileName(change.RelativePath);
        CloudDirectory parent = GetDirectory(parentPath);
        if (change.Kind is CloudRemoteChangeKind.FileUpsert)
        {
            CloudFilePlaceholderSpec specification = CloudFilePlaceholderSpec
                .CreateBuilder(name, identity, change.Length!.Value)
                .WithMetadata(change.Metadata!)
                .WithInSyncState(true)
                .Build();
            await parent.CreatePlaceholderAsync(specification, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        CloudDirectoryPlaceholderSpec directory = CloudDirectoryPlaceholderSpec
            .CreateBuilder(name, identity)
            .WithMetadata(change.Metadata!)
            .WithInSyncState(true)
            .Build();
        await parent.CreatePlaceholderAsync(directory, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask UpdateRemotePlaceholderAsync(
        CloudRemoteChange change,
        CloudItemState localState,
        CancellationToken cancellationToken)
    {
        CloudPlaceholderIdentity identity = new(
            localState.ItemId,
            change.RemoteId,
            change.RemoteRevision);
        CloudPlaceholderPatch.Builder builder = CloudPlaceholderPatch.CreateBuilder()
            .WithIdentity(identity)
            .WithMetadata(change.Metadata!)
            .WithInSyncState(true);
        if (change.Kind is CloudRemoteChangeKind.FileUpsert)
        {
            builder.WithFileSize(change.Length!.Value);
        }

        CloudItem item = CreateItemReference(change.RelativePath, change.ItemKind);
        await item.UpdatePlaceholderAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
    }

    private string ToFullPath(string relativePath) =>
        Path.Combine(SyncRootPath, relativePath);

    private static CloudRemoteConflict CreateConflict(
        CloudRemoteChange change,
        CloudItemState? localState,
        CloudRemoteConflictReason reason) =>
        new(change, localState, reason, DateTimeOffset.UtcNow);

    private async ValueTask<CloudRemoteBatchState> PersistRemoteBatchOutcomeAsync(
        CloudRemoteChangeBatch batch,
        CloudRemoteBatchState previous,
        int appliedEntryCount,
        CloudRemoteChange change,
        RemoteEntryOutcome outcome,
        Guid? conflictId,
        ReadOnlyMemory<byte> cursor,
        CloudRemoteBatchStatus status,
        CancellationToken cancellationToken)
    {
        await using ICloudStateTransaction transaction = await (_stateStore ?? throw new InvalidOperationException(
                "The cloud file system has no open state store.")).BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (outcome.Conflict is not null)
        {
            Guid id = conflictId ?? Guid.NewGuid();
            await transaction.Conflicts.UpsertAsync(
                new CloudConflictState(
                    id,
                    outcome.Conflict.LocalState?.ItemId,
                    ToDurableConflictKind(outcome.Conflict.Reason),
                    EncodeConflict(outcome.Conflict),
                    outcome.Conflict.DetectedAt),
                cancellationToken).ConfigureAwait(false);
        }

        CloudRemoteBatchState next = new(
            batch.BatchId,
            cursor.Span,
            appliedEntryCount,
            batch.Changes.Count,
            status,
            previous.Payload.Span,
            DateTimeOffset.UtcNow,
            batch.Fingerprint,
            change.ChangeId);
        await transaction.RemoteBatches.UpsertAsync(next, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return next;
    }

    private async ValueTask<CloudRemoteBatchState> PersistRemoteBatchFailureAsync(
        CloudRemoteChangeBatch batch,
        CloudRemoteBatchState previous,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using ICloudStateTransaction transaction = await (_stateStore ?? throw new InvalidOperationException(
                "The cloud file system has no open state store.")).BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        CloudRemoteBatchState next = new(
            batch.BatchId,
            previous.Cursor.Span,
            previous.AppliedEntryCount,
            batch.Changes.Count,
            CloudRemoteBatchStatus.Failed,
            Encoding.UTF8.GetBytes($"failure|{exception.GetType().FullName}"),
            DateTimeOffset.UtcNow,
            batch.Fingerprint,
            previous.LastAppliedChangeId);
        await transaction.RemoteBatches.UpsertAsync(next, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return next;
    }

    private static CloudRemoteApplyResult CreateRemoteApplyResult(
        CloudRemoteBatchState progress,
        IReadOnlyList<CloudRemoteApplyEntryResult> entries,
        bool requiresRetry,
        IReadOnlyList<Guid> conflictIds) =>
        new(
            entries,
            progress.Status,
            progress.Cursor.Span,
            requiresRetry,
            progress.AppliedEntryCount,
            conflictIds);

    private static byte[] EncodeConflict(CloudRemoteConflict conflict) =>
        Encoding.UTF8.GetBytes(
            $"{RemoteConflictPayloadPrefix}{conflict.Change.ChangeId}|" +
            $"{conflict.Change.RemoteId}|{conflict.Change.RemoteRevision}|" +
            $"{conflict.Reason}|{conflict.Change.RelativePath}");

    private static CloudStateConflictKind ToDurableConflictKind(CloudRemoteConflictReason reason) =>
        reason switch
        {
            CloudRemoteConflictReason.Content => CloudStateConflictKind.Content,
            CloudRemoteConflictReason.Move => CloudStateConflictKind.Move,
            CloudRemoteConflictReason.Delete => CloudStateConflictKind.Delete,
            _ => CloudStateConflictKind.Metadata,
        };

    private sealed record RemoteEntryContext(
        CloudItemState? LocalState,
        CloudItemState? ByRemoteId,
        CloudItemState? ByItemId,
        CloudItemState? ByPath,
        IReadOnlyList<CloudOperationJournalEntry> LocalOperations);

    private sealed record RemoteEntryOutcome(
        CloudRemoteApplyEntryStatus Status,
        CloudRemoteConflict? Conflict)
    {
        internal static RemoteEntryOutcome AppliedResult { get; } = new(
            CloudRemoteApplyEntryStatus.Applied,
            null);

        internal static RemoteEntryOutcome ConflictResult(CloudRemoteConflict conflict) => new(
            CloudRemoteApplyEntryStatus.Conflict,
            conflict);
    }
}
