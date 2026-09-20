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

        bytes[0] = 9;

        Assert.True(checkpoint.Value.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.True(operation.Payload.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
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
    }
}
