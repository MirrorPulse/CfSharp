using System.Diagnostics;
using System.Runtime.Versioning;

using CfSharp.Tests.Persistence;

namespace CfSharp.Tests;

[SupportedOSPlatform("windows10.0.16299")]
public sealed class CloudLocalChangeFeedSoakTests
{
    [Fact]
    [Trait("Category", "LongSoak")]
    public async Task FeedAndDispatcherRemainStableDuringContinuousRun()
    {
        TimeSpan duration = ReadSoakDuration();
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            "CfSharp-long-soak",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        Process process = Process.GetCurrentProcess();
        process.Refresh();
        long baselinePrivateBytes = process.PrivateMemorySize64;
        int baselineHandleCount = process.HandleCount;
        int baselineThreadCount = process.Threads.Count;
        long maximumPrivateBytes = baselinePrivateBytes;
        int maximumHandleCount = baselineHandleCount;
        int maximumThreadCount = baselineThreadCount;
        int cycles = 0;
        int emittedChanges = 0;
        int dispatchedItems = 0;
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;

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
                    BufferCapacity = 256,
                    BatchSize = 32,
                },
                source);
            await using CloudProviderDispatcher dispatcher = new(new CloudProviderSessionOptions
            {
                QueueCapacity = 512,
                WorkerCount = 8,
                MaxConcurrentDataRequests = 4,
                MaxConcurrentPlaceholderRequests = 2,
            });
            await feed.StartAsync();

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < duration)
            {
                cycles++;
                const int changesPerCycle = 64;
                for (int index = 0; index < changesPerCycle; index++)
                {
                    int changeNumber = emittedChanges++;
                    await source.EmitAsync(new(
                        LocalChangeSourceAction.Created,
                        $"long-soak-{changeNumber:D8}.txt"));
                }

                await DrainFeedAsync(feed, changesPerCycle);
                int cycleDispatches = await DispatchCycleAsync(dispatcher);
                dispatchedItems += cycleDispatches;
                SampleProcessResources(
                    process,
                    ref maximumPrivateBytes,
                    ref maximumHandleCount,
                    ref maximumThreadCount);

                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            await dispatcher.DisposeAsync(TimeSpan.FromSeconds(30));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            process.Refresh();
            long finalPrivateBytes = process.PrivateMemorySize64;
            int finalHandleCount = process.HandleCount;
            int finalThreadCount = process.Threads.Count;
            SampleProcessResources(
                process,
                ref maximumPrivateBytes,
                ref maximumHandleCount,
                ref maximumThreadCount);

            Assert.True(cycles > 1, "The long-soak test must execute multiple continuous cycles.");
            Assert.Equal(emittedChanges, cycles * 64);
            Assert.True(dispatchedItems >= cycles * 64);
            Assert.InRange(
                finalHandleCount,
                baselineHandleCount,
                baselineHandleCount + 128);
            Assert.InRange(
                finalThreadCount,
                baselineThreadCount,
                baselineThreadCount + 32);
            Assert.InRange(
                finalPrivateBytes,
                baselinePrivateBytes,
                baselinePrivateBytes + (256L * 1024 * 1024));

            WriteSoakArtifact(new
            {
                status = "passed",
                durationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 3),
                cycles,
                emittedChanges,
                dispatchedItems,
                baselinePrivateBytes,
                finalPrivateBytes,
                maximumPrivateBytes,
                baselineHandleCount,
                finalHandleCount,
                maximumHandleCount,
                baselineThreadCount,
                finalThreadCount,
                maximumThreadCount,
                startedUtc,
                completedUtc = DateTimeOffset.UtcNow,
                commit = ResolveRepositoryCommit(),
            });
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

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

    private static TimeSpan ReadSoakDuration()
    {
        string? secondsValue = Environment.GetEnvironmentVariable("CFSHARP_SOAK_DURATION_SECONDS");
        if (int.TryParse(secondsValue, out int seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(Math.Min(seconds, 86_400));
        }

        string? minutesValue = Environment.GetEnvironmentVariable("CFSHARP_SOAK_DURATION_MINUTES");
        return int.TryParse(minutesValue, out int minutes) && minutes > 0
            ? TimeSpan.FromMinutes(Math.Min(minutes, 1_440))
            : TimeSpan.FromSeconds(30);
    }

    private static async Task DrainFeedAsync(CloudLocalChangeFeed feed, int expectedChanges)
    {
        HashSet<Guid> acknowledged = [];
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        while (acknowledged.Count < expectedChanges)
        {
            CloudLocalChangeBatch batch = await feed.ReadBatchAsync(timeout.Token);
            Assert.False(batch.RequiresFullRescan);
            Assert.NotEmpty(batch.Changes);
            await feed.AcknowledgeAsync(batch.Changes.Select(change =>
                new CloudLocalChangeAcknowledgement(change.OperationId)), timeout.Token);
            foreach (CloudLocalChange change in batch.Changes)
            {
                Assert.True(acknowledged.Add(change.OperationId));
            }
        }
    }

    private static async Task<int> DispatchCycleAsync(CloudProviderDispatcher dispatcher)
    {
        const int itemCount = 64;
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource[] completions = Enumerable.Range(0, itemCount)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        int[] terminalCounts = new int[itemCount];

        for (int index = 0; index < itemCount; index++)
        {
            int itemIndex = index;
            CloudProviderWorkItem workItem = new(
                CloudProviderRequestKind.FetchData,
                cancellation,
                async token =>
                {
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    RecordTerminal(itemIndex);
                },
                () => RecordTerminal(itemIndex),
                _ => RecordTerminal(itemIndex));

            Assert.True(dispatcher.TryEnqueue(workItem));
        }

        await Task.WhenAll(completions.Select(completion => completion.Task))
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(terminalCounts, count => Assert.Equal(1, count));
        return itemCount;

        void RecordTerminal(int itemIndex)
        {
            if (Interlocked.Increment(ref terminalCounts[itemIndex]) == 1)
            {
                completions[itemIndex].TrySetResult();
            }
        }
    }

    private static void SampleProcessResources(
        Process process,
        ref long maximumPrivateBytes,
        ref int maximumHandleCount,
        ref int maximumThreadCount)
    {
        process.Refresh();
        maximumPrivateBytes = Math.Max(maximumPrivateBytes, process.PrivateMemorySize64);
        maximumHandleCount = Math.Max(maximumHandleCount, process.HandleCount);
        maximumThreadCount = Math.Max(maximumThreadCount, process.Threads.Count);
    }

    private static string ResolveRepositoryCommit()
    {
        string? repositoryRoot = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            repositoryRoot = Directory.GetCurrentDirectory();
        }

        try
        {
            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static void WriteSoakArtifact(object evidence)
    {
        string? artifactPath = Environment.GetEnvironmentVariable("CFSHARP_SOAK_ARTIFACT");
        if (string.IsNullOrWhiteSpace(artifactPath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(artifactPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, System.Text.Json.JsonSerializer.Serialize(evidence));
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
