using System.Runtime.Versioning;

using CfSharp.Tests.Persistence;

namespace CfSharp.Tests;

[SupportedOSPlatform("windows10.0.16299")]
public sealed class CloudFileSystemRemoteChangeTests
{
    [Fact]
    public async Task EmptyRemoteBatchPublishesFinalCursorAndReplaysIdempotently()
    {
        string rootPath = CreateRoot();
        try
        {
            await using CloudFileSystem fileSystem = await StartAsync(rootPath);
            CloudRemoteChangeBatch batch = new(
                "empty-batch",
                new byte[] { 1, 2 },
                [],
                new byte[] { 3, 4 });

            CloudRemoteApplyResult first = await fileSystem.ApplyRemoteChangesAsync(batch);
            Assert.Equal(CloudRemoteBatchStatus.Applied, first.Status);
            Assert.False(first.RequiresRetry);
            Assert.True(first.SafeCursor.Span.SequenceEqual(new byte[] { 3, 4 }));

            CloudRemoteApplyResult replay = await fileSystem.ApplyRemoteChangesAsync(batch);
            Assert.Equal(CloudRemoteBatchStatus.Applied, replay.Status);
            Assert.Empty(replay.Entries);
            Assert.False(replay.RequiresRetry);
        }
        finally
        {
            DeleteRoot(rootPath);
        }
    }

    [Fact]
    public async Task MissingRemoteMoveBecomesDurableConflictAndRejectsChangedFingerprint()
    {
        string rootPath = CreateRoot();
        try
        {
            await using CloudFileSystem fileSystem = await StartAsync(rootPath);
            CloudRemoteChange move = new(
                "move-1",
                CloudRemoteChangeKind.Move,
                "remote-file",
                "revision-2",
                CloudItemKind.File,
                "new.txt",
                previousRelativePath: "old.txt");
            CloudRemoteChangeBatch batch = new("batch-1", Array.Empty<byte>(), [move], new byte[] { 9 });

            CloudRemoteApplyResult failed = await fileSystem.ApplyRemoteChangesAsync(batch);
            CloudRemoteApplyEntryResult entry = Assert.Single(failed.Entries);
            Assert.Equal(CloudRemoteApplyEntryStatus.Conflict, entry.Status);
            Assert.False(failed.RequiresRetry);
            Assert.True(failed.SafeCursor.Span.SequenceEqual(new byte[] { 9 }));

            CloudRemoteChange changed = new(
                "move-1",
                CloudRemoteChangeKind.Move,
                "remote-file",
                "revision-3",
                CloudItemKind.File,
                "other.txt",
                previousRelativePath: "old.txt");
            CloudRemoteChangeBatch changedBatch = new(
                "batch-1",
                Array.Empty<byte>(),
                [changed],
                new byte[] { 9 });

            await Assert.ThrowsAsync<ArgumentException>(() => fileSystem
                .ApplyRemoteChangesAsync(changedBatch)
                .AsTask());
        }
        finally
        {
            DeleteRoot(rootPath);
        }
    }

    [Fact]
    public async Task DurableConflictEnvelopeCanBeReadAndDeferred()
    {
        string rootPath = CreateRoot();
        try
        {
            await using CloudFileSystem fileSystem = await StartAsync(rootPath);
            CloudRemoteChange move = new(
                "move-round-trip",
                CloudRemoteChangeKind.Move,
                "remote-file",
                "revision-2",
                CloudItemKind.File,
                "new.txt",
                previousRelativePath: "old.txt",
                metadata: CloudPlaceholderMetadata.CreateFileBuilder()
                    .WithAttributes(FileAttributes.ReadOnly)
                    .WithLastWriteTime(DateTimeOffset.UtcNow.AddMinutes(-1))
                    .Build());
            CloudRemoteChangeBatch batch = new(
                "batch-round-trip",
                Array.Empty<byte>(),
                [move],
                new byte[] { 7 });

            CloudRemoteApplyResult applied = await fileSystem.ApplyRemoteChangesAsync(batch);
            Guid conflictId = Assert.Single(applied.ConflictIds);

            CloudRemoteApplyEntryResult deferred = await fileSystem.ResolveRemoteConflictAsync(
                conflictId,
                new CloudRemoteConflictResolution(CloudRemoteConflictDecision.Defer));
            Assert.Equal(CloudRemoteApplyEntryStatus.Conflict, deferred.Status);
            Assert.NotNull(deferred.Conflict);
            Assert.Equal(move.ChangeId, deferred.Conflict!.Change.ChangeId);
            Assert.Equal(move.RelativePath, deferred.Conflict.Change.RelativePath);
            Assert.Equal(move.Metadata!.Attributes, deferred.Conflict.Change.Metadata!.Attributes);
            Assert.Equal(CloudRemoteConflictReason.MissingItem, deferred.Conflict.Reason);
        }
        finally
        {
            DeleteRoot(rootPath);
        }
    }

    private static async Task<CloudFileSystem> StartAsync(string rootPath)
    {
        CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(rootPath, new TestRuntime())
            .WithStateStore(InMemoryCloudStateStoreContractTests.CreateFactoryForTesting())
            .Build();
        await fileSystem.StartAsync();
        return fileSystem;
    }

    private static string CreateRoot()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "CfSharp-remote-change-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class TestRuntime : ICloudFileSystemRuntime
    {
        public ICloudFileSystemRuntimeSession Start(
            string syncRootPath,
            SyncRootRegistrationOptions? registration,
            ICloudFileContentProvider? contentProvider,
            ICloudStateStore stateStore) => new TestRuntimeSession();
    }

    private sealed class TestRuntimeSession : ICloudFileSystemRuntimeSession
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
