namespace CfSharp.Tests;

public sealed class CloudProviderDispatcherTests
{
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
}
