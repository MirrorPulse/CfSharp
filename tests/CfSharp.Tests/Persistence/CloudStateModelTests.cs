namespace CfSharp.Tests.Persistence;

public sealed class CloudStateModelTests
{
    [Fact]
    public void BinaryValuesOwnDefensiveCopies()
    {
        byte[] bytes = [1, 2, 3];
        CloudStateCheckpoint checkpoint = new("cursor", bytes, DateTimeOffset.UtcNow);
        CloudOperationJournalEntry operation = new(
            Guid.NewGuid(),
            CloudStateOperationKind.Create,
            null,
            bytes,
            DateTimeOffset.UtcNow);
        CloudRemoteBatchState batch = new(
            "batch",
            bytes,
            1,
            1,
            CloudRemoteBatchStatus.Applied,
            bytes,
            DateTimeOffset.UtcNow,
            bytes,
            "change-1");

        bytes[0] = 9;

        Assert.True(checkpoint.Value.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.True(operation.Payload.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.True(batch.Cursor.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.True(batch.Fingerprint.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.Equal("change-1", batch.LastAppliedChangeId);

        CloudEchoSuppressionState suppression = new(
            Guid.NewGuid(),
            null,
            CloudStateOperationKind.Move,
            "destination.txt",
            bytes,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "source.txt",
            2);
        bytes[1] = 8;

        Assert.True(suppression.Payload.Span.SequenceEqual(new byte[] { 9, 2, 3 }));
        Assert.Equal("source.txt", suppression.PreviousRelativePath);
        Assert.Equal(2, suppression.RemainingObservations);
    }

    [Fact]
    public void ContextNormalizesAbsolutePath()
    {
        string path = Path.Combine(Path.GetTempPath(), "CfSharp-context", ".");

        CloudStateStoreContext context = new(path);

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
            context.SyncRootPath);
    }

    [Fact]
    public void ModelsRejectInvalidIdentityAndEnumValues()
    {
        Assert.Throws<ArgumentException>(() => new CloudItemState(
            Guid.Empty,
            "remote",
            "file.txt",
            CloudItemKind.File,
            null,
            null,
            false,
            DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudRemoteBatchState(
            "batch",
            [],
            2,
            1,
            CloudRemoteBatchStatus.Applying,
            [],
            DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudEchoSuppressionState(
            Guid.NewGuid(),
            null,
            (CloudStateOperationKind)999,
            "file.txt",
            [],
            DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudEchoSuppressionState(
            Guid.NewGuid(),
            null,
            CloudStateOperationKind.Create,
            "file.txt",
            [],
            DateTimeOffset.UtcNow,
            remainingObservations: 0));
    }

    [Fact]
    public void IdentityBoundEchoSuppressionFailsClosedWithoutObservedIdentity()
    {
        Guid itemId = Guid.NewGuid();
        CloudEchoSuppressionState suppression = new(
            Guid.NewGuid(),
            itemId,
            CloudStateOperationKind.ContentUpdate,
            "file.txt",
            [],
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.False(suppression.Matches(
            CloudStateOperationKind.ContentUpdate,
            "file.txt",
            previousRelativePath: null,
            observedItemId: null));
        Assert.False(suppression.Matches(
            CloudStateOperationKind.ContentUpdate,
            "file.txt",
            previousRelativePath: null,
            observedItemId: Guid.NewGuid()));
        Assert.True(suppression.Matches(
            CloudStateOperationKind.ContentUpdate,
            "file.txt",
            previousRelativePath: null,
            observedItemId: itemId));
    }
}
