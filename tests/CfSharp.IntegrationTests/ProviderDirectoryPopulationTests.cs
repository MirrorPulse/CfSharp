using System.Diagnostics;
using System.Runtime.Versioning;
using CfSharp.Storage.Sqlite;

namespace CfSharp.IntegrationTests;

public sealed class ProviderDirectoryPopulationTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task PartialDirectoryRequestsAnOrderedProviderPage()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string testPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        string rootPath = Path.Combine(testPath, "root");
        string databasePath = Path.Combine(testPath, "state", "cfsharp.db");
        Directory.CreateDirectory(rootPath);
        Guid providerId = Guid.NewGuid();
        CloudFileSystem? fileSystem = null;
        CloudSyncRoot? root = null;
        bool registered = false;
        DemandProvider provider = new();

        try
        {
            SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
                .CreateBuilder($"CfSharp Population {providerId:N}", "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
                .WithPopulationPolicy(CloudPopulationPolicy.Full)
                .WithRootMarkedInSync()
                .Build();

            fileSystem = CloudFileSystem.CreateBuilder(rootPath)
                .WithStateStore(new SqliteCloudStateStoreFactory(databasePath))
                .WithRegistration(registration)
                .WithContentProvider(provider)
                .Build();
            await fileSystem.StartAsync();
            root = CloudSyncRoot.Open(rootPath);
            registered = true;

            CloudDirectoryPlaceholderSpec directory = CloudDirectoryPlaceholderSpec
                .CreateBuilder("remote", "remote-directory")
                .WithPopulationState(CloudDirectoryPopulationState.Partial)
                .WithInSyncState(true)
                .Build();
            await fileSystem.Root.CreatePlaceholderAsync(directory);
            await fileSystem.GetDirectory("remote")
                .SetPopulationStateAsync(CloudDirectoryPopulationState.Partial);
            CloudItemSnapshot directorySnapshot = await fileSystem
                .GetDirectory("remote")
                .InspectAsync();
            Assert.True(
                directorySnapshot.PlaceholderState.HasFlag(CloudPlaceholderState.Partial),
                $"Expected a partial directory, got {directorySnapshot.PlaceholderState}.");

            _ = Directory.GetFileSystemEntries(Path.Combine(rootPath, "remote"));
            using (Process enumerationProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c dir /b \"{Path.Combine(rootPath, "remote")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            })!)
            {
                await enumerationProcess.WaitForExitAsync();
            }
            CloudProviderFetchPlaceholdersRequest request =
                await provider.Request.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(
                request.NormalizedPath.EndsWith(
                    Path.Combine("remote"),
                    StringComparison.OrdinalIgnoreCase),
                $"Unexpected normalized callback path: {request.NormalizedPath}");
            Assert.Equal("*", request.SearchPattern);
            Assert.True(File.Exists(Path.Combine(rootPath, "remote", "child.txt")));
            CloudItemSnapshot childSnapshot = await fileSystem
                .GetFile("remote/child.txt")
                .InspectAsync();
            Assert.Equal("remote-child", childSnapshot.RemoteId);
            Assert.NotNull(childSnapshot.DurableStateUpdatedAt);
            Assert.Equal(1, provider.RequestCount);

            await fileSystem.DisposeAsync();
            fileSystem = null;
            root.Unregister();
            registered = false;
        }
        finally
        {
            if (fileSystem is not null)
            {
                await fileSystem.DisposeAsync();
            }

            if (registered && root is not null)
            {
                try
                {
                    root.Unregister();
                }
                catch (CloudFilesException)
                {
                    // Preserve the original failure while still attempting cleanup.
                }
            }

            if (Directory.Exists(testPath))
            {
                Directory.Delete(testPath, recursive: true);
            }
        }
    }

    private sealed class DemandProvider : ICloudDemandProvider
    {
        public TaskCompletionSource<CloudProviderFetchPlaceholdersRequest> Request { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestCount { get; private set; }

        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException();
        }

        public ValueTask<CloudProviderDirectoryPage> FetchChildrenAsync(
            CloudProviderFetchPlaceholdersRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            Request.TrySetResult(request);
            CloudFilePlaceholderSpec child = CloudFilePlaceholderSpec
                .CreateBuilder("child.txt", "remote-child", 3)
                .WithInSyncState(false)
                .Build();
            return ValueTask.FromResult(new CloudProviderDirectoryPage([child], totalCount: 1));
        }
    }
}
