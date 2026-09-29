using System.Runtime.Versioning;

namespace CfSharp.Storage.Sqlite.Tests;

[SupportedOSPlatform("windows10.0.16299")]
public sealed class RemoteConflictDismissalTests
{
    [Fact]
    public async Task DismissalPreservesLocalContentAndIsAtomicAndDurable()
    {
        string area = Path.Combine(Path.GetTempPath(), "CfSharp-conflict-dismiss", Guid.NewGuid().ToString("N"));
        string root = Path.Combine(area, "root");
        Directory.CreateDirectory(root);
        string localPath = Path.Combine(root, "local.txt");
        await File.WriteAllTextAsync(localPath, "unuploaded local content");
        FailingFactory factory = new(new SqliteCloudStateStoreFactory(Path.Combine(area, "state.db")));
        try
        {
            Guid id;
            await using (CloudFileSystem fileSystem = Create(root, factory))
            {
                await fileSystem.StartAsync();
                id = await CreateConflictAsync(fileSystem);
                Assert.Equal(CloudRemoteApplyEntryStatus.Conflict,
                    (await fileSystem.ResolveRemoteConflictAsync(id,
                        new CloudRemoteConflictResolution(CloudRemoteConflictDecision.KeepLocal))).Status);
                factory.FailCommit = true;
                await Assert.ThrowsAsync<IOException>(() => fileSystem.DismissRemoteConflictAsync(id).AsTask());
                factory.FailCommit = false;
                Assert.Equal(CloudRemoteConflictDismissalStatus.Dismissed,
                    await fileSystem.DismissRemoteConflictAsync(id));
                Assert.Equal(CloudRemoteConflictDismissalStatus.AlreadyDismissed,
                    await fileSystem.DismissRemoteConflictAsync(id));
                await Assert.ThrowsAsync<ArgumentException>(() => fileSystem.DismissRemoteConflictAsync(Guid.Empty).AsTask());
            }

            await using (CloudFileSystem restarted = Create(root, factory))
            {
                await restarted.StartAsync();
                Assert.Equal(CloudRemoteConflictDismissalStatus.AlreadyDismissed,
                    await restarted.DismissRemoteConflictAsync(id));
                Assert.Equal(CloudRemoteConflictDismissalStatus.NotFound,
                    await restarted.DismissRemoteConflictAsync(Guid.NewGuid()));
                Assert.Equal("unuploaded local content", await File.ReadAllTextAsync(localPath));
            }

            await using ICloudStateStore store = await factory.OpenAsync(new CloudStateStoreContext(root));
            await using ICloudStateTransaction transaction = await store.BeginTransactionAsync();
            Assert.Empty(await transaction.Conflicts.ListAsync());
            Assert.Empty(await transaction.Operations.ListAsync(10));
            Assert.Equal(new byte[] { 9 }, (await transaction.RemoteBatches.GetAsync("batch"))!.Cursor.ToArray());
        }
        finally
        {
            Directory.Delete(area, recursive: true);
        }
    }

    private sealed class FailingFactory(ICloudStateStoreFactory inner) : ICloudStateStoreFactory
    {
        internal bool FailCommit { get; set; }

        public async ValueTask<ICloudStateStore> OpenAsync(CloudStateStoreContext context,
            CancellationToken cancellationToken = default) =>
            new Store(await inner.OpenAsync(context, cancellationToken), this);

        private sealed class Store(ICloudStateStore inner, FailingFactory owner) : ICloudStateStore
        {
            public async ValueTask<ICloudStateTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
                new Transaction(await inner.BeginTransactionAsync(cancellationToken), owner);

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }

        private sealed class Transaction(ICloudStateTransaction inner, FailingFactory owner) : ICloudStateTransaction
        {
            public ICloudItemStateRepository Items => inner.Items;
            public ICloudCheckpointRepository Checkpoints => inner.Checkpoints;
            public ICloudOperationJournal Operations => inner.Operations;
            public ICloudConflictRepository Conflicts => inner.Conflicts;
            public ICloudRemoteBatchRepository RemoteBatches => inner.RemoteBatches;
            public ICloudEchoSuppressionRepository EchoSuppressions => inner.EchoSuppressions;
            public ValueTask CommitAsync(CancellationToken cancellationToken = default) =>
                owner.FailCommit ? ValueTask.FromException(new IOException("Injected commit failure.")) : inner.CommitAsync(cancellationToken);
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
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
