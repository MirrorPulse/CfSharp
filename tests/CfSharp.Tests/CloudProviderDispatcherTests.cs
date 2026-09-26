namespace CfSharp.Tests;

public sealed class CloudProviderDispatcherTests
{
    [Fact]
    [Trait("Category", "Soak")]
    public async Task DeterministicCallbackStormCompletesEachAcceptedItemExactlyOnce()
    {
        const int itemCount = 400;
        CloudProviderSessionOptions options = new()
        {
            QueueCapacity = itemCount,
            WorkerCount = 8,
            MaxConcurrentDataRequests = 4,
            MaxConcurrentPlaceholderRequests = 1,
        };
        await using CloudProviderDispatcher dispatcher = new(options);
        int[] terminalCounts = new int[itemCount];
        int dataStarted = 0;
        int placeholderStarted = 0;
        int terminalItems = 0;
        TaskCompletionSource allTerminal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        for (int index = 0; index < itemCount; index++)
        {
            int itemIndex = index;
            CloudProviderRequestKind kind = index % 5 == 0
                ? CloudProviderRequestKind.FetchPlaceholders
                : CloudProviderRequestKind.FetchData;
            CloudProviderWorkItem workItem = new(
                kind,
                new CancellationTokenSource(),
                async token =>
                {
                    if (kind == CloudProviderRequestKind.FetchData)
                    {
                        Interlocked.Increment(ref dataStarted);
                    }
                    else
                    {
                        Interlocked.Increment(ref placeholderStarted);
                    }

                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    if (itemIndex % 17 == 0)
                    {
                        throw new InvalidOperationException("deterministic callback storm failure");
                    }

                    RecordTerminal(itemIndex);
                },
                () => RecordTerminal(itemIndex),
                _ => RecordTerminal(itemIndex));

            Assert.True(dispatcher.TryEnqueue(workItem));
        }

        void RecordTerminal(int itemIndex)
        {
            if (Interlocked.Increment(ref terminalCounts[itemIndex]) == 1 &&
                Interlocked.Increment(ref terminalItems) == itemCount)
            {
                allTerminal.TrySetResult();
            }
        }

        await allTerminal.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await dispatcher.DisposeAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(itemCount, terminalItems);
        Assert.All(terminalCounts, count => Assert.Equal(1, count));
        Assert.True(dataStarted > 0);
        Assert.True(placeholderStarted > 0);
    }

    [Fact]
    public async Task BoundedQueueRejectsNewWorkAndCancelsIt()
    {
        CloudProviderSessionOptions options = new()
        {
            QueueCapacity = 1,
            WorkerCount = 1,
            MaxConcurrentDataRequests = 1,
        };
        await using CloudProviderDispatcher dispatcher = new(options);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource firstCancellation = new();
        CloudProviderWorkItem first = new(
            CloudProviderRequestKind.FetchData,
            firstCancellation,
            async _ =>
            {
                started.TrySetResult();
                await release.Task;
            },
            () => { },
            _ => { });

        Assert.True(dispatcher.TryEnqueue(first));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        TaskCompletionSource queuedExecuted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CloudProviderWorkItem queued = new(
            CloudProviderRequestKind.FetchData,
            new CancellationTokenSource(),
            _ =>
            {
                queuedExecuted.TrySetResult();
                return ValueTask.CompletedTask;
            },
            () => { },
            _ => { });
        Assert.True(dispatcher.TryEnqueue(queued));

        TaskCompletionSource rejectedCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CloudProviderWorkItem rejected = new(
            CloudProviderRequestKind.FetchData,
            new CancellationTokenSource(),
            _ => ValueTask.CompletedTask,
            () => rejectedCancellation.TrySetResult(),
            _ => { });
        Assert.False(dispatcher.TryEnqueue(rejected));
        await rejectedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        release.TrySetResult();
        await queuedExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShutdownCancelsQueuedAndRunningWork()
    {
        CloudProviderSessionOptions options = new() { QueueCapacity = 4, WorkerCount = 1 };
        CloudProviderDispatcher dispatcher = new(options);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CloudProviderWorkItem work = new(
            CloudProviderRequestKind.FetchData,
            new CancellationTokenSource(),
            async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
            () => cancelled.TrySetResult(),
            _ => { });

        Assert.True(dispatcher.TryEnqueue(work));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DisposeAsync(TimeSpan.FromSeconds(5));
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AvailableKindIsNotBlockedByAnotherKindWaitingForItsPermit()
    {
        CloudProviderSessionOptions options = new()
        {
            QueueCapacity = 8,
            WorkerCount = 2,
            MaxConcurrentDataRequests = 1,
            MaxConcurrentPlaceholderRequests = 1,
        };
        CloudProviderDispatcher dispatcher = new(options);
        TaskCompletionSource firstDataStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirstData = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource placeholderStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        CloudProviderWorkItem firstData = new(
            CloudProviderRequestKind.FetchData,
            new CancellationTokenSource(),
            async _ =>
            {
                firstDataStarted.TrySetResult();
                await releaseFirstData.Task;
            },
            () => { },
            _ => { });
        CloudProviderWorkItem secondData = new(
            CloudProviderRequestKind.FetchData,
            new CancellationTokenSource(),
            _ => ValueTask.CompletedTask,
            () => { },
            _ => { });
        CloudProviderWorkItem placeholder = new(
            CloudProviderRequestKind.FetchPlaceholders,
            new CancellationTokenSource(),
            _ =>
            {
                placeholderStarted.TrySetResult();
                return ValueTask.CompletedTask;
            },
            () => { },
            _ => { });

        Assert.True(dispatcher.TryEnqueue(firstData));
        await firstDataStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(dispatcher.TryEnqueue(secondData));
        Assert.True(dispatcher.TryEnqueue(placeholder));

        await placeholderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseFirstData.TrySetResult();
        await dispatcher.DisposeAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShutdownIgnoresRequestTokenDisposedByCompletionRace()
    {
        CloudProviderSessionOptions options = new() { QueueCapacity = 2, WorkerCount = 2 };
        CloudProviderDispatcher dispatcher = new(options);
        CancellationTokenSource cancellation = new();
        TaskCompletionSource tokenDisposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CloudProviderWorkItem work = new(
            CloudProviderRequestKind.FetchData,
            cancellation,
            async _ =>
            {
                cancellation.Dispose();
                tokenDisposed.TrySetResult();
                await release.Task;
            },
            () => { },
            _ => { });

        Assert.True(dispatcher.TryEnqueue(work));
        await tokenDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposal = dispatcher.DisposeAsync(TimeSpan.FromSeconds(5)).AsTask();
        release.TrySetResult();
        await disposal;
    }
}
