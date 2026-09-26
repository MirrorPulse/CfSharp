using System.Runtime.Versioning;

using CfSharp.Tests.Persistence;

namespace CfSharp.Tests;

[SupportedOSPlatform("windows10.0.16299")]
public sealed class CloudLocalChangeFeedSoakTests
{
    [Fact]
    [Trait("Category", "Soak")]
    public async Task FeedSustainsJournaledChangesAndAcknowledgements()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            "CfSharp-local-feed-soak",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        try
        {
            await using ICloudStateStore store = await InMemoryCloudStateContract
                .OpenAsync(rootPath);
            SyntheticChangeSource source = new();
            await using CloudLocalChangeFeed feed = CloudLocalChangeFeed.CreateForTesting(
                rootPath,
                store,
                new CloudLocalChangeFeedOptions
                {
                    BufferCapacity = 1024,
                    BatchSize = 32,
                },
                source);
            await feed.StartAsync();

            const int changeCount = 512;
            for (int index = 0; index < changeCount; index++)
            {
                await source.EmitAsync(new(
                    LocalChangeSourceAction.Created,
                    $"soak-{index:D4}.txt"));
            }

            HashSet<Guid> acknowledged = [];
            using CancellationTokenSource readTimeout = new(TimeSpan.FromSeconds(30));
            while (acknowledged.Count < changeCount)
            {
                CloudLocalChangeBatch batch = await feed.ReadBatchAsync(readTimeout.Token);
                Assert.False(batch.RequiresFullRescan);
                Assert.NotEmpty(batch.Changes);
                await feed.AcknowledgeAsync(batch.Changes.Select(change =>
                    new CloudLocalChangeAcknowledgement(change.OperationId)), readTimeout.Token);
                foreach (CloudLocalChange change in batch.Changes)
                {
                    Assert.True(acknowledged.Add(change.OperationId));
                }
            }

            using CancellationTokenSource emptyReadTimeout = new(TimeSpan.FromMilliseconds(250));
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await feed.ReadBatchAsync(emptyReadTimeout.Token));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private sealed class SyntheticChangeSource : ILocalChangeSource
    {
        private Func<LocalChangeSourceEvent, ValueTask>? _eventHandler;

        public Task StartAsync(
            Func<LocalChangeSourceEvent, ValueTask> eventHandler,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _eventHandler = eventHandler;
            return Task.CompletedTask;
        }

        public ValueTask EmitAsync(LocalChangeSourceEvent sourceEvent)
        {
            Func<LocalChangeSourceEvent, ValueTask> handler =
                _eventHandler ?? throw new InvalidOperationException("The source has not started.");
            return handler(sourceEvent);
        }

        public ValueTask DisposeAsync()
        {
            _eventHandler = null;
            return ValueTask.CompletedTask;
        }
    }

    private static class InMemoryCloudStateContract
    {
        public static async ValueTask<ICloudStateStore> OpenAsync(string rootPath)
        {
            ICloudStateStoreFactory factory = InMemoryCloudStateStoreContractTests
                .CreateFactoryForTesting();
            return await factory.OpenAsync(new CloudStateStoreContext(rootPath));
        }
    }
}
