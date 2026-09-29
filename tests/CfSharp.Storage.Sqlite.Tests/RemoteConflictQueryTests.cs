using System.Runtime.Versioning;

namespace CfSharp.Storage.Sqlite.Tests;

[SupportedOSPlatform("windows10.0.16299")]
public sealed class RemoteConflictQueryTests
{
    [Fact]
    public async Task QueriesRestoreDecodedConflictsAfterReopeningAndDoNotResolveThem()
    {
        string area = Path.Combine(Path.GetTempPath(), "CfSharp-conflict-query", Guid.NewGuid().ToString("N"));
        string root = Path.Combine(area, "root");
        Directory.CreateDirectory(root);
        SqliteCloudStateStoreFactory factory = new(Path.Combine(area, "state.db"));
        try
        {
            Guid id;
            await using (CloudFileSystem fileSystem = Create(root, factory))
            {
                await fileSystem.StartAsync();
                id = await CreateConflictAsync(fileSystem);
            }

            await using (CloudFileSystem restarted = Create(root, factory))
            {
                await restarted.StartAsync();
                CloudRemoteConflictRecord record = Assert.Single(await restarted.ListRemoteConflictsAsync());
                Assert.Equal(id, record.ConflictId);
                Assert.Equal(root, record.SyncRootPath);
                Assert.Equal("move", record.Conflict.Change.ChangeId);
                Assert.Equal("missing.txt", record.Conflict.Change.PreviousRelativePath);
                Assert.Equal(CloudRemoteConflictReason.MissingItem, record.Conflict.Reason);
                Assert.Null(record.Conflict.LocalState);
                Assert.Equal(id, (await restarted.GetRemoteConflictAsync(id))!.ConflictId);
                Assert.Null(await restarted.GetRemoteConflictAsync(Guid.NewGuid()));
                await Assert.ThrowsAsync<ArgumentException>(() => restarted.GetRemoteConflictAsync(Guid.Empty).AsTask());
                using CancellationTokenSource canceled = new();
                canceled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    restarted.ListRemoteConflictsAsync(canceled.Token).AsTask());
                Assert.Single(await restarted.ListRemoteConflictsAsync());
            }

            await using (ICloudStateStore store = await factory.OpenAsync(new CloudStateStoreContext(root)))
            {
                await using ICloudStateTransaction transaction = await store.BeginTransactionAsync();
                CloudConflictState state = Assert.Single(await transaction.Conflicts.ListAsync());
                Assert.Equal(id, state.ConflictId);
                Assert.Equal(new byte[] { 9 }, (await transaction.RemoteBatches.GetAsync("batch"))!.Cursor.ToArray());
                await transaction.Conflicts.UpsertAsync(new CloudConflictState(state.ConflictId,
                    state.ItemId, state.Kind, new byte[] { 0xff }, state.CreatedAt));
                await transaction.CommitAsync();
            }

            await using CloudFileSystem corrupted = Create(root, factory);
            await corrupted.StartAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => corrupted.ListRemoteConflictsAsync().AsTask());
            await Assert.ThrowsAsync<InvalidDataException>(() => corrupted.GetRemoteConflictAsync(id).AsTask());
        }
        finally
        {
            Directory.Delete(area, recursive: true);
        }
    }

    private sealed class TestRuntime : ICloudFileSystemRuntime
    {
        public ICloudFileSystemRuntimeSession Start(string syncRootPath,
            SyncRootRegistrationOptions? registration, ICloudFileContentProvider? contentProvider,
            ICloudStateStore stateStore) => new Session();

        private sealed class Session : ICloudFileSystemRuntimeSession
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static CloudFileSystem Create(string root, ICloudStateStoreFactory factory) =>
        CloudFileSystem.CreateBuilder(root, new TestRuntime()).WithStateStore(factory).Build();

    private static async Task<Guid> CreateConflictAsync(CloudFileSystem fileSystem)
    {
        CloudRemoteChange change = new("move", CloudRemoteChangeKind.Move, "remote", "r2",
            CloudItemKind.File, "new.txt", previousRelativePath: "missing.txt");
        CloudRemoteApplyResult result = await fileSystem.ApplyRemoteChangesAsync(
            new CloudRemoteChangeBatch("batch", Array.Empty<byte>(), [change], new byte[] { 9 }));
        return Assert.Single(result.ConflictIds);
    }
}
