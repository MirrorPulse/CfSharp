using System.Runtime.Versioning;

namespace CfSharp.IntegrationTests;

public sealed class CloudFileSystemLifecycleTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task StartAndDisposeReleaseProcessResourcesButPreserveRegistration()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string rootPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        Guid providerId = Guid.NewGuid();
        TrackingStore store = new();
        CloudFileSystem? fileSystem = null;
        CloudSyncRoot? root = null;
        bool registered = false;
        Directory.CreateDirectory(rootPath);

        try
        {
            SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
                .CreateBuilder($"CfSharp File System {providerId:N}", "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
                .WithPopulationPolicy(CloudPopulationPolicy.Partial)
                .WithRootMarkedInSync()
                .Build();
            fileSystem = CloudFileSystem.CreateBuilder(rootPath)
                .WithStateStore(new SingleStoreFactory(store))
                .WithRegistration(registration)
                .WithContentProvider(new StubContentProvider())
                .Build();

            await fileSystem.StartAsync();
            Assert.Equal(CloudFileSystemLifecycleState.Started, fileSystem.LifecycleState);
            root = CloudSyncRoot.Open(rootPath);
            registered = true;
            Assert.Equal(registration.ProviderName, root.GetInfo().ProviderName);

            await fileSystem.DisposeAsync();

            Assert.Equal(CloudFileSystemLifecycleState.Disposed, fileSystem.LifecycleState);
            Assert.Equal(1, store.DisposeCalls);
            Assert.Equal(registration.ProviderName, root.GetInfo().ProviderName);

            // Successful unregister proves the facade disconnected its provider session first.
            root.Unregister();
            registered = false;
        }
        finally
        {
            if (fileSystem is not null)
            {
                try
                {
                    await fileSystem.DisposeAsync();
                }
                catch (Exception)
                {
                    // Preserve the original test failure while still cleaning persistent state.
                }
            }

            if (!registered)
            {
                try
                {
                    root = CloudSyncRoot.Open(rootPath);
                    registered = true;
                }
                catch (CloudFilesException)
                {
                    // No registration was created, or it was already removed successfully.
                }
            }

            if (registered && root is not null)
            {
                try
                {
                    root.Unregister();
                }
                catch (CloudFilesException)
                {
                    // Preserve the original test failure while still attempting system cleanup.
                }
            }

            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private sealed class SingleStoreFactory : ICloudStateStoreFactory
    {
        private readonly TrackingStore _store;

        internal SingleStoreFactory(TrackingStore store)
        {
            _store = store;
        }

        public ValueTask<ICloudStateStore> OpenAsync(
            CloudStateStoreContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ICloudStateStore>(_store);
        }
    }

    private sealed class TrackingStore : ICloudStateStore
    {
        internal int DisposeCalls { get; private set; }

        public ValueTask<ICloudStateTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
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
