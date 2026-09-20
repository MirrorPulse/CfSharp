namespace CfSharp.Tests.Persistence;

public abstract class CloudStateStoreContractTests
{
    protected abstract ICloudStateStoreFactory CreateFactory();

    [Fact]
    public async Task CommittedItemSurvivesStoreReopen()
    {
        ICloudStateStoreFactory factory = CreateFactory();
        CloudStateStoreContext context = CreateContext();
        Guid itemId = Guid.NewGuid();
        DateTimeOffset timestamp = new(2026, 9, 20, 12, 30, 0, TimeSpan.FromHours(8));
        CloudItemState expected = new(
            itemId,
            "remote-42",
            "Documents/report.txt",
            CloudItemKind.File,
            "revision-7",
            1234,
            false,
            timestamp);

        await using (ICloudStateStore store = await factory.OpenAsync(context))
        await using (ICloudStateTransaction transaction = await store.BeginTransactionAsync())
        {
            await transaction.Items.UpsertAsync(expected);
            await transaction.CommitAsync();
        }

        await using ICloudStateStore reopened = await factory.OpenAsync(context);
        await using ICloudStateTransaction read = await reopened.BeginTransactionAsync();
        CloudItemState? byId = await read.Items.GetByItemIdAsync(itemId);
        CloudItemState? byRemote = await read.Items.GetByRemoteIdAsync("remote-42");
        CloudItemState? byPath = await read.Items.GetByRelativePathAsync("Documents/report.txt");

        AssertItem(expected, byId);
        AssertItem(expected, byRemote);
        AssertItem(expected, byPath);
        await read.RollbackAsync();
    }

    [Fact]
    public async Task DisposalAndExplicitRollbackDiscardWrites()
    {
        ICloudStateStoreFactory factory = CreateFactory();
        await using ICloudStateStore store = await factory.OpenAsync(CreateContext());

        await using (ICloudStateTransaction disposed = await store.BeginTransactionAsync())
        {
            await disposed.Checkpoints.UpsertAsync(
                new CloudStateCheckpoint("disposed", [1], DateTimeOffset.UtcNow));
        }

        await using (ICloudStateTransaction rolledBack = await store.BeginTransactionAsync())
        {
            await rolledBack.Checkpoints.UpsertAsync(
                new CloudStateCheckpoint("rolled-back", [2], DateTimeOffset.UtcNow));
            await rolledBack.RollbackAsync();
        }

        await using ICloudStateTransaction read = await store.BeginTransactionAsync();
        Assert.Null(await read.Checkpoints.GetAsync("disposed"));
        Assert.Null(await read.Checkpoints.GetAsync("rolled-back"));
        await read.RollbackAsync();
    }

    [Fact]
    public async Task TransactionCommitsEveryRepositoryAtomically()
    {
        ICloudStateStoreFactory factory = CreateFactory();
        await using ICloudStateStore store = await factory.OpenAsync(CreateContext());
        Guid itemId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        Guid conflictId = Guid.NewGuid();
        Guid suppressionId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

        await using (ICloudStateTransaction write = await store.BeginTransactionAsync())
        {
            await write.Checkpoints.UpsertAsync(new CloudStateCheckpoint("remote", [1, 2], now));
            CloudOperationJournalEntry queued = await write.Operations.EnqueueAsync(
                new CloudOperationJournalEntry(
                    operationId,
                    CloudStateOperationKind.ContentUpdate,
                    itemId,
                    [3, 4],
                    now));
            Assert.Equal(1, queued.Sequence);
            await write.Conflicts.UpsertAsync(
                new CloudConflictState(
                    conflictId,
                    itemId,
                    CloudStateConflictKind.Content,
                    [5, 6],
                    now));
            await write.RemoteBatches.UpsertAsync(
                new CloudRemoteBatchState(
                    "batch-1",
                    [7],
                    2,
                    5,
                    CloudRemoteBatchStatus.Applying,
                    [8],
                    now));
            await write.EchoSuppressions.UpsertAsync(
                new CloudEchoSuppressionState(
                    suppressionId,
                    itemId,
                    CloudStateOperationKind.ContentUpdate,
                    "file.txt",
                    [9],
                    now.AddMinutes(5)));
            await write.CommitAsync();
        }

        await using ICloudStateTransaction read = await store.BeginTransactionAsync();
        CloudStateCheckpoint? checkpoint = await read.Checkpoints.GetAsync("remote");
        CloudOperationJournalEntry? operation = await read.Operations.GetAsync(operationId);
        CloudConflictState? conflict = await read.Conflicts.GetAsync(conflictId);
        CloudRemoteBatchState? batch = await read.RemoteBatches.GetAsync("batch-1");
        CloudEchoSuppressionState? suppression = await read.EchoSuppressions.GetAsync(suppressionId);

        Assert.NotNull(checkpoint);
        Assert.True(checkpoint.Value.Span.SequenceEqual(new byte[] { 1, 2 }));
        Assert.NotNull(operation);
        Assert.Equal(1, operation.Sequence);
        Assert.True(operation.Payload.Span.SequenceEqual(new byte[] { 3, 4 }));
        Assert.NotNull(conflict);
        Assert.True(conflict.Payload.Span.SequenceEqual(new byte[] { 5, 6 }));
        Assert.NotNull(batch);
        Assert.Equal(2, batch.AppliedEntryCount);
        Assert.NotNull(suppression);
        Assert.Single(await read.EchoSuppressions.ListActiveAsync(now));
        await read.RollbackAsync();
    }

    [Fact]
    public async Task JournalPreservesSequenceAndRetryUpdates()
    {
        ICloudStateStoreFactory factory = CreateFactory();
        await using ICloudStateStore store = await factory.OpenAsync(CreateContext());
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();

        await using (ICloudStateTransaction write = await store.BeginTransactionAsync())
        {
            CloudOperationJournalEntry first = await write.Operations.EnqueueAsync(
                new CloudOperationJournalEntry(
                    firstId,
                    CloudStateOperationKind.Create,
                    null,
                    [1],
                    now));
            CloudOperationJournalEntry second = await write.Operations.EnqueueAsync(
                new CloudOperationJournalEntry(
                    secondId,
                    CloudStateOperationKind.Delete,
                    null,
                    [2],
                    now.AddSeconds(1)));
            await write.Operations.UpdateAsync(
                new CloudOperationJournalEntry(
                    first.OperationId,
                    first.Kind,
                    first.ItemId,
                    first.Payload.Span,
                    first.CreatedAt,
                    1,
                    now.AddMinutes(1),
                    first.Sequence));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await write.Operations.UpdateAsync(
                    new CloudOperationJournalEntry(
                        first.OperationId,
                        first.Kind,
                        first.ItemId,
                        first.Payload.Span,
                        first.CreatedAt,
                        first.AttemptCount,
                        first.RetryAfter,
                        first.Sequence + 10)));
            Assert.Equal(2, second.Sequence);
            await write.CommitAsync();
        }

        await using ICloudStateTransaction read = await store.BeginTransactionAsync();
        IReadOnlyList<CloudOperationJournalEntry> operations =
            await read.Operations.ListAsync(10);
        Assert.Equal(new[] { firstId, secondId }, operations.Select(item => item.OperationId));
        Assert.Equal(1, operations[0].AttemptCount);
        Assert.Equal(now.AddMinutes(1), operations[0].RetryAfter);
        await read.RollbackAsync();
    }

    [Fact]
    public async Task ExpiredEchoSuppressionsCanBeRemoved()
    {
        ICloudStateStoreFactory factory = CreateFactory();
        await using ICloudStateStore store = await factory.OpenAsync(CreateContext());
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using (ICloudStateTransaction write = await store.BeginTransactionAsync())
        {
            await write.EchoSuppressions.UpsertAsync(
                new CloudEchoSuppressionState(
                    Guid.NewGuid(),
                    null,
                    CloudStateOperationKind.Move,
                    "old.txt",
                    [],
                    now.AddSeconds(-1)));
            await write.EchoSuppressions.UpsertAsync(
                new CloudEchoSuppressionState(
                    Guid.NewGuid(),
                    null,
                    CloudStateOperationKind.Move,
                    "new.txt",
                    [],
                    now.AddMinutes(1)));
            await write.EchoSuppressions.RemoveExpiredAsync(now);
            await write.CommitAsync();
        }

        await using ICloudStateTransaction read = await store.BeginTransactionAsync();
        CloudEchoSuppressionState active = Assert.Single(
            await read.EchoSuppressions.ListActiveAsync(now));
        Assert.Equal("new.txt", active.RelativePath);
        await read.RollbackAsync();
    }

    [Fact]
    public async Task TerminalTransactionRejectsRepositoryAndCommitReuse()
    {
        ICloudStateStoreFactory factory = CreateFactory();
        await using ICloudStateStore store = await factory.OpenAsync(CreateContext());
        await using ICloudStateTransaction transaction = await store.BeginTransactionAsync();

        await transaction.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transaction.Checkpoints.GetAsync("after-commit"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transaction.CommitAsync());
    }

    [Fact]
    public async Task FactoryAndStoreHonorPreCanceledTokens()
    {
        ICloudStateStoreFactory factory = CreateFactory();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await factory.OpenAsync(CreateContext(), cancellation.Token));

        await using ICloudStateStore store = await factory.OpenAsync(CreateContext());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await store.BeginTransactionAsync(cancellation.Token));
    }

    private static CloudStateStoreContext CreateContext() =>
        new(Path.Combine(Path.GetTempPath(), "CfSharp-contract", Guid.NewGuid().ToString("N")));

    private static void AssertItem(CloudItemState expected, CloudItemState? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.ItemId, actual.ItemId);
        Assert.Equal(expected.RemoteId, actual.RemoteId);
        Assert.Equal(expected.RelativePath, actual.RelativePath);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.RemoteRevision, actual.RemoteRevision);
        Assert.Equal(expected.LocalFileId, actual.LocalFileId);
        Assert.Equal(expected.IsTombstone, actual.IsTombstone);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
    }
}
