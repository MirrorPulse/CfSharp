using System.Runtime.Versioning;
using CfSharp.Tests.Persistence;

namespace CfSharp.Tests;

[SupportedOSPlatform("windows10.0.16299")]
public sealed class CloudFileSystemLocalChangeFeedTests
{
    [Fact]
    public async Task StartedFileSystemOwnsAndStopsItsLocalChangeFeed()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            "CfSharp-local-feed-facade",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        try
        {
            await using CloudFileSystem fileSystem = CloudFileSystem
                .CreateBuilder(rootPath, new TestRuntime())
                .WithStateStore(InMemoryCloudStateStoreContractTests.CreateFactoryForTesting())
                .Build();
            await fileSystem.StartAsync();

            CloudLocalChangeFeed feed = fileSystem.CreateLocalChangeFeed(
                new CloudLocalChangeFeedOptions { BufferCapacity = 8, BatchSize = 8 });
            Assert.Same(feed, fileSystem.CreateLocalChangeFeed());
            await feed.StartAsync();

            string filePath = Path.Combine(rootPath, "facade.txt");
            await File.WriteAllTextAsync(filePath, "hello");
            CloudLocalChangeBatch batch = await feed.ReadBatchAsync(
                new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            Assert.Contains(batch.Changes, change =>
                change.RelativePath.Equals("facade.txt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
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
