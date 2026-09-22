using System.Runtime.Versioning;

namespace CfSharp.Tests;

[SupportedOSPlatform("windows10.0.16299")]
public sealed class CloudFileSystemTests
{
    [Fact]
    public void BuilderRequiresAbsoluteExistingRootAndStateStore()
    {
        Assert.Throws<ArgumentException>(() => CloudFileSystem.CreateBuilder("relative-root"));

        string missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Throws<DirectoryNotFoundException>(() =>
            CloudFileSystem.CreateBuilder(missingPath)
                .WithStateStore(new RecordingStoreFactory())
                .Build());

        using TestDirectory root = new();
        Assert.Throws<InvalidOperationException>(() =>
            CloudFileSystem.CreateBuilder(root.Path).Build());
    }

    [Fact]
    public async Task StartAndDisposeOwnResourcesInDeterministicOrder()
    {
        using TestDirectory root = new();
        List<string> events = [];
        RecordingStore store = new(events);
        RecordingStoreFactory factory = new(store, events);
        RecordingRuntime runtime = new(events);
        await using CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(root.Path, runtime)
            .WithStateStore(factory)
            .Build();

        Assert.Equal(CloudFileSystemLifecycleState.Created, fileSystem.LifecycleState);
        await fileSystem.StartAsync();

        Assert.Equal(CloudFileSystemLifecycleState.Started, fileSystem.LifecycleState);
        Assert.Equal(Path.GetFullPath(root.Path), fileSystem.SyncRootPath);
        Assert.Equal(fileSystem.SyncRootPath, factory.Context?.SyncRootPath);
        Assert.Equal(2, events.Count);
        Assert.Equal("factory.open", events[0]);
        Assert.Equal("runtime.start", events[1]);

        await fileSystem.DisposeAsync();
        await fileSystem.DisposeAsync();

        Assert.Equal(CloudFileSystemLifecycleState.Disposed, fileSystem.LifecycleState);
        Assert.Equal(4, events.Count);
        Assert.Equal("factory.open", events[0]);
        Assert.Equal("runtime.start", events[1]);
        Assert.Equal("runtime.dispose", events[2]);
        Assert.Equal("store.dispose", events[3]);
        Assert.Equal(1, store.DisposeCalls);
        Assert.Equal(1, runtime.Session.DisposeCalls);
    }

    [Fact]
    public async Task RootIsRevalidatedBeforeFactoryOpen()
    {
        TestDirectory root = new();
        RecordingStoreFactory factory = new();
        RecordingRuntime runtime = new();
        await using CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(root.Path, runtime)
            .WithStateStore(factory)
            .Build();
        root.Dispose();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
            await fileSystem.StartAsync());

        Assert.Equal(0, factory.OpenCalls);
        Assert.Equal(0, runtime.StartCalls);
        Assert.Equal(CloudFileSystemLifecycleState.Created, fileSystem.LifecycleState);
    }

    [Fact]
    public async Task StoreOpenFailureDoesNotInvokeRuntimeAndCanBeRetried()
    {
        using TestDirectory root = new();
        RecordingStore store = new();
        RecordingStoreFactory factory = new()
        {
            OpenFailure = new IOException("open failed"),
        };
        RecordingRuntime runtime = new();
        await using CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(root.Path, runtime)
            .WithStateStore(factory)
            .Build();

        await Assert.ThrowsAsync<IOException>(async () => await fileSystem.StartAsync());
        Assert.Equal(CloudFileSystemLifecycleState.Created, fileSystem.LifecycleState);
        Assert.Equal(0, runtime.StartCalls);

        factory.OpenFailure = null;
        factory.Store = store;
        await fileSystem.StartAsync();

        Assert.Equal(2, factory.OpenCalls);
        Assert.Equal(1, runtime.StartCalls);
        Assert.Equal(CloudFileSystemLifecycleState.Started, fileSystem.LifecycleState);
    }

    [Fact]
    public async Task RuntimeFailureDisposesOpenedStoreAndCanBeRetried()
    {
        using TestDirectory root = new();
        RecordingStore firstStore = new();
        RecordingStore secondStore = new();
        RecordingStoreFactory factory = new(firstStore);
        RecordingRuntime runtime = new()
        {
            StartFailure = new InvalidOperationException("runtime failed"),
        };
        await using CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(root.Path, runtime)
            .WithStateStore(factory)
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fileSystem.StartAsync());

        Assert.Equal(1, firstStore.DisposeCalls);
        Assert.Equal(CloudFileSystemLifecycleState.Created, fileSystem.LifecycleState);

        runtime.StartFailure = null;
        factory.Store = secondStore;
        await fileSystem.StartAsync();

        Assert.Equal(CloudFileSystemLifecycleState.Started, fileSystem.LifecycleState);
        Assert.Equal(2, runtime.StartCalls);
    }

    [Fact]
    public async Task CleanupFailureMakesFailedStartupTerminal()
    {
        using TestDirectory root = new();
        RecordingStore store = new()
        {
            DisposeFailure = new IOException("store cleanup failed"),
        };
        RecordingRuntime runtime = new()
        {
            StartFailure = new InvalidOperationException("runtime failed"),
        };
        CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(root.Path, runtime)
            .WithStateStore(new RecordingStoreFactory(store))
            .Build();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(async () =>
            await fileSystem.StartAsync());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Equal(CloudFileSystemLifecycleState.Stopping, fileSystem.LifecycleState);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await fileSystem.StartAsync());
        store.DisposeFailure = null;
        await fileSystem.DisposeAsync();
        Assert.Equal(CloudFileSystemLifecycleState.Disposed, fileSystem.LifecycleState);
    }

    [Fact]
    public async Task DisposalAttemptsRuntimeAndStoreWhenBothFail()
    {
        using TestDirectory root = new();
        RecordingStore store = new()
        {
            DisposeFailure = new IOException("store dispose failed"),
        };
        RecordingRuntime runtime = new();
        runtime.Session.DisposeFailure = new IOException("runtime dispose failed");
        CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(root.Path, runtime)
            .WithStateStore(new RecordingStoreFactory(store))
            .Build();
        await fileSystem.StartAsync();

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(async () =>
            await fileSystem.DisposeAsync());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Equal(1, runtime.Session.DisposeCalls);
        Assert.Equal(1, store.DisposeCalls);
        Assert.Equal(CloudFileSystemLifecycleState.Stopping, fileSystem.LifecycleState);
        runtime.Session.DisposeFailure = null;
        store.DisposeFailure = null;
        await fileSystem.DisposeAsync();
        Assert.Equal(2, runtime.Session.DisposeCalls);
        Assert.Equal(2, store.DisposeCalls);
        Assert.Equal(CloudFileSystemLifecycleState.Disposed, fileSystem.LifecycleState);
    }

    [Fact]
    public async Task CancellationAndTerminalStatesRejectStartupWithoutAcquiringResources()
    {
        using TestDirectory root = new();
        RecordingStoreFactory factory = new();
        RecordingRuntime runtime = new();
        CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(root.Path, runtime)
            .WithStateStore(factory)
            .Build();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fileSystem.StartAsync(cancellation.Token));
        Assert.Equal(0, factory.OpenCalls);

        await fileSystem.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fileSystem.StartAsync());
        await fileSystem.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await fileSystem.StartAsync());
    }

    [Fact]
    public async Task BuilderPassesRegistrationAndContentProviderToRuntime()
    {
        using TestDirectory root = new();
        SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
            .CreateBuilder("Example Cloud", "1.0.0")
            .Build();
        StubContentProvider contentProvider = new();
        RecordingRuntime runtime = new();
        await using CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(root.Path, runtime)
            .WithStateStore(new RecordingStoreFactory())
            .WithRegistration(registration)
            .WithContentProvider(contentProvider)
            .Build();

        await fileSystem.StartAsync();

        Assert.Same(registration, runtime.Registration);
        Assert.Same(contentProvider, runtime.ContentProvider);
        Assert.Equal(fileSystem.SyncRootPath, runtime.SyncRootPath);
    }

    [Fact]
    public async Task DisposalDrainsAdmittedOperationsBeforeReleasingResources()
    {
        using TestDirectory root = new();
        RecordingStore store = new();
        RecordingRuntime runtime = new();
        CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(root.Path, runtime)
            .WithStateStore(new RecordingStoreFactory(store))
            .Build();
        await fileSystem.StartAsync();
        CloudFileSystem.CloudFileSystemOperationLease operation =
            await fileSystem.AcquireOperationAsync(
                [CloudItemOperationScope.Subtree(root.Path)]);

        Task disposal = fileSystem.DisposeAsync().AsTask();
        await WaitForStateAsync(fileSystem, CloudFileSystemLifecycleState.Stopping);

        Assert.False(disposal.IsCompleted);
        Assert.Equal(0, runtime.Session.DisposeCalls);
        Assert.Equal(0, store.DisposeCalls);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await fileSystem.AcquireOperationAsync(
                [CloudItemOperationScope.Exact(Path.Combine(root.Path, "new.bin"))]));

        operation.Dispose();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(CloudFileSystemLifecycleState.Disposed, fileSystem.LifecycleState);
        Assert.Equal(1, runtime.Session.DisposeCalls);
        Assert.Equal(1, store.DisposeCalls);
    }

    private static async Task WaitForStateAsync(
        CloudFileSystem fileSystem,
        CloudFileSystemLifecycleState expected)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (fileSystem.LifecycleState != expected)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        private int _disposed;

        internal TestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CfSharp-file-system-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 && Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class RecordingStoreFactory : ICloudStateStoreFactory
    {
        private readonly List<string>? _events;

        internal RecordingStoreFactory(RecordingStore? store = null, List<string>? events = null)
        {
            Store = store ?? new RecordingStore(events);
            _events = events;
        }

        internal int OpenCalls { get; private set; }

        internal CloudStateStoreContext? Context { get; private set; }

        internal RecordingStore Store { get; set; }

        internal Exception? OpenFailure { get; set; }

        public ValueTask<ICloudStateStore> OpenAsync(
            CloudStateStoreContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCalls++;
            Context = context;
            _events?.Add("factory.open");
            if (OpenFailure is not null)
            {
                throw OpenFailure;
            }

            return ValueTask.FromResult<ICloudStateStore>(Store);
        }
    }

    private sealed class RecordingStore : ICloudStateStore
    {
        private readonly List<string>? _events;

        internal RecordingStore(List<string>? events = null)
        {
            _events = events;
        }

        internal int DisposeCalls { get; private set; }

        internal Exception? DisposeFailure { get; set; }

        public ValueTask<ICloudStateTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            _events?.Add("store.dispose");
            return DisposeFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(DisposeFailure);
        }
    }

    private sealed class RecordingRuntime : ICloudFileSystemRuntime
    {
        private readonly List<string>? _events;

        internal RecordingRuntime(List<string>? events = null)
        {
            _events = events;
            Session = new RecordingRuntimeSession(events);
        }

        internal int StartCalls { get; private set; }

        internal string? SyncRootPath { get; private set; }

        internal SyncRootRegistrationOptions? Registration { get; private set; }

        internal ICloudFileContentProvider? ContentProvider { get; private set; }

        internal Exception? StartFailure { get; set; }

        internal RecordingRuntimeSession Session { get; }

        public ICloudFileSystemRuntimeSession Start(
            string syncRootPath,
            SyncRootRegistrationOptions? registration,
            ICloudFileContentProvider? contentProvider,
            ICloudStateStore stateStore)
        {
            StartCalls++;
            SyncRootPath = syncRootPath;
            Registration = registration;
            ContentProvider = contentProvider;
            _events?.Add("runtime.start");
            if (StartFailure is not null)
            {
                throw StartFailure;
            }

            return Session;
        }
    }

    private sealed class RecordingRuntimeSession : ICloudFileSystemRuntimeSession
    {
        private readonly List<string>? _events;

        internal RecordingRuntimeSession(List<string>? events)
        {
            _events = events;
        }

        internal int DisposeCalls { get; private set; }

        internal Exception? DisposeFailure { get; set; }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            _events?.Add("runtime.dispose");
            return DisposeFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(DisposeFailure);
        }
    }

    private sealed class StubContentProvider : ICloudFileContentProvider
    {
        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
