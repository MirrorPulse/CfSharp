using System.Runtime.Versioning;

namespace CfSharp.Tests;

[SupportedOSPlatform("windows10.0.16299")]
public sealed class CloudItemTests
{
    [Fact]
    public async Task InspectionReturnsFreshImmutableFileAndRootSnapshots()
    {
        using TestDirectory root = new();
        string filePath = Path.Combine(root.Path, "report.txt");
        await File.WriteAllTextAsync(filePath, "one");
        await using CloudFileSystem fileSystem = await StartAsync(root.Path, new InspectionStore());

        CloudFile file = fileSystem.GetFile(@"folder\..\report.txt");
        CloudItemSnapshot first = await file.InspectAsync();
        await File.WriteAllTextAsync(filePath, "second-value");
        CloudItemSnapshot second = await file.InspectAsync();
        CloudItemSnapshot rootSnapshot = await fileSystem.Root.InspectAsync();

        Assert.Equal("report.txt", file.Name);
        Assert.Equal("report.txt", file.RelativePath);
        Assert.Equal(Path.GetFullPath(filePath), file.FullPath);
        Assert.Same(fileSystem.Root.GetType(), file.Parent?.GetType());
        Assert.Equal(3, first.Length);
        Assert.Equal(12, second.Length);
        Assert.Equal(3, first.Length);
        Assert.True(first.Exists);
        Assert.False(first.IsPlaceholder);
        Assert.Equal(CloudContentAvailability.NotApplicable, first.ContentAvailability);
        Assert.Equal(CloudItemKind.Directory, rootSnapshot.Kind);
        Assert.True(rootSnapshot.Exists);
        Assert.Null(rootSnapshot.Length);
        Assert.Null(fileSystem.Root.Parent);
    }

    [Fact]
    public async Task MissingLocalItemStillReturnsDurableTombstoneState()
    {
        using TestDirectory root = new();
        Guid itemId = Guid.NewGuid();
        DateTimeOffset updatedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        CloudItemState state = new(
            itemId,
            "remote-gone",
            "gone.txt",
            CloudItemKind.File,
            "revision-4",
            42,
            true,
            updatedAt);
        await using CloudFileSystem fileSystem = await StartAsync(
            root.Path,
            new InspectionStore(state));

        CloudItemSnapshot snapshot = await fileSystem.GetFile("gone.txt").InspectAsync();

        Assert.False(snapshot.Exists);
        Assert.Equal(itemId, snapshot.ItemId);
        Assert.Equal("remote-gone", snapshot.RemoteId);
        Assert.Equal("revision-4", snapshot.RemoteRevision);
        Assert.Equal(42, snapshot.LocalFileId);
        Assert.True(snapshot.IsTombstone);
        Assert.Equal(updatedAt, snapshot.DurableStateUpdatedAt);
    }

    [Fact]
    public async Task InspectionRejectsLocalAndDurableKindMismatches()
    {
        using TestDirectory root = new();
        Directory.CreateDirectory(Path.Combine(root.Path, "folder"));
        CloudItemState directoryState = new(
            Guid.NewGuid(),
            "remote-directory",
            "missing.txt",
            CloudItemKind.Directory,
            null,
            null,
            false,
            DateTimeOffset.UtcNow);
        await using CloudFileSystem fileSystem = await StartAsync(
            root.Path,
            new InspectionStore(directoryState));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fileSystem.GetFile("folder").InspectAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fileSystem.GetFile("missing.txt").InspectAsync());
    }

    [Fact]
    public async Task ItemPathsRejectRootedTraversalAndLinkedEscapes()
    {
        using TestDirectory root = new();
        using TestDirectory outside = new();
        string linkPath = Path.Combine(root.Path, "outside-link");
        Directory.CreateSymbolicLink(linkPath, outside.Path);
        await using CloudFileSystem fileSystem = await StartAsync(root.Path, new InspectionStore());

        Assert.Throws<ArgumentException>(() => fileSystem.GetFile(@"..\outside.txt"));
        Assert.Throws<ArgumentException>(() => fileSystem.GetFile(outside.Path));
        Assert.Throws<ArgumentException>(() => fileSystem.GetFile(string.Empty));
        Assert.Throws<ArgumentException>(() => fileSystem.GetDirectory("outside-link"));
    }

    [Fact]
    public async Task ItemOperationsRequireStartedOwner()
    {
        using TestDirectory root = new();
        InspectionStore store = new();
        StubRuntime runtime = new();
        CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(root.Path, runtime)
            .WithStateStore(new SingleStoreFactory(store))
            .Build();

        Assert.Throws<InvalidOperationException>(() => _ = fileSystem.Root);
        await fileSystem.StartAsync();
        CloudFile file = fileSystem.GetFile("later.txt");
        await fileSystem.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => fileSystem.GetFile("later.txt"));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await file.InspectAsync());
    }

    [Fact]
    public void SnapshotOwnsPlaceholderIdentityBytes()
    {
        byte[] identity = [1, 2, 3];
        CloudItemSnapshot snapshot = new(
            CloudItemKind.File,
            true,
            FileAttributes.Offline,
            10,
            null,
            null,
            null,
            CloudPlaceholderState.Placeholder | CloudPlaceholderState.Partial,
            CloudContentAvailability.OnlineOnly,
            CloudPinState.Unpinned,
            CloudSynchronizationState.InSync,
            1,
            2,
            0,
            0,
            0,
            0,
            identity,
            null,
            DateTimeOffset.UtcNow);

        identity[0] = 9;

        Assert.Equal(1, snapshot.PlaceholderIdentity.Span[0]);
    }

    private static async Task<CloudFileSystem> StartAsync(string rootPath, InspectionStore store)
    {
        CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(rootPath, new StubRuntime())
            .WithStateStore(new SingleStoreFactory(store))
            .Build();
        await fileSystem.StartAsync();
        return fileSystem;
    }

    private sealed class TestDirectory : IDisposable
    {
        internal TestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CfSharp-item-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class SingleStoreFactory : ICloudStateStoreFactory
    {
        private readonly InspectionStore _store;

        internal SingleStoreFactory(InspectionStore store)
        {
            _store = store;
        }

        public ValueTask<ICloudStateStore> OpenAsync(
            CloudStateStoreContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ICloudStateStore>(_store);
        }
    }

    private sealed class InspectionStore : ICloudStateStore
    {
        private readonly Dictionary<string, CloudItemState> _items;

        internal InspectionStore(params CloudItemState[] items)
        {
            _items = items.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        }

        public ValueTask<ICloudStateTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ICloudStateTransaction>(new InspectionTransaction(_items));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InspectionTransaction : ICloudStateTransaction, ICloudItemStateRepository
    {
        private readonly IReadOnlyDictionary<string, CloudItemState> _items;
        private bool _disposed;

        internal InspectionTransaction(IReadOnlyDictionary<string, CloudItemState> items)
        {
            _items = items;
        }

        public ICloudItemStateRepository Items => this;

        public ICloudCheckpointRepository Checkpoints => throw new NotSupportedException();

        public ICloudOperationJournal Operations => throw new NotSupportedException();

        public ICloudConflictRepository Conflicts => throw new NotSupportedException();

        public ICloudRemoteBatchRepository RemoteBatches => throw new NotSupportedException();

        public ICloudEchoSuppressionRepository EchoSuppressions => throw new NotSupportedException();

        public ValueTask<CloudItemState?> GetByItemIdAsync(
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_items.Values.SingleOrDefault(item => item.ItemId == itemId));

        public ValueTask<CloudItemState?> GetByRemoteIdAsync(
            string remoteId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_items.Values.SingleOrDefault(
                item => string.Equals(item.RemoteId, remoteId, StringComparison.Ordinal)));

        public ValueTask<CloudItemState?> GetByRelativePathAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _items.TryGetValue(relativePath, out CloudItemState? item);
            return ValueTask.FromResult(item);
        }

        public ValueTask UpsertAsync(
            CloudItemState item,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask RemoveAsync(
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask CommitAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubRuntime : ICloudFileSystemRuntime
    {
        public ICloudFileSystemRuntimeSession Start(
            string syncRootPath,
            SyncRootRegistrationOptions? registration,
            ICloudFileContentProvider? contentProvider) =>
            new StubRuntimeSession();
    }

    private sealed class StubRuntimeSession : ICloudFileSystemRuntimeSession
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
