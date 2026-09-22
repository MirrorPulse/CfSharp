using System.Threading.Channels;

namespace CfSharp;

/// <summary>Describes one callback envelope waiting for provider dispatch.</summary>
internal sealed class CloudProviderWorkItem
{
    private readonly Func<CancellationToken, ValueTask> _handler;
    private readonly Action _cancelled;
    private readonly Action<Exception> _failed;
    private int _completed;

    internal CloudProviderWorkItem(
        CloudProviderRequestKind kind,
        CancellationTokenSource cancellation,
        Func<CancellationToken, ValueTask> handler,
        Action cancelled,
        Action<Exception> failed)
    {
        Kind = kind;
        Cancellation = cancellation;
        _handler = handler;
        _cancelled = cancelled;
        _failed = failed;
    }

    internal CloudProviderRequestKind Kind { get; }

    internal CancellationTokenSource Cancellation { get; }

    internal async ValueTask RunAsync()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        try
        {
            await _handler(Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Cancellation.IsCancellationRequested)
        {
            _cancelled();
        }
        catch (Exception exception)
        {
            _failed(exception);
        }
    }

    internal void Cancel()
    {
        Cancellation.Cancel();
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _cancelled();
        }
    }

    internal void Fail(Exception exception)
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            _failed(exception);
        }
    }
}

/// <summary>Runs callback work behind a bounded queue and per-kind concurrency limits.</summary>
/// <remarks>
/// The dispatcher owns no native memory and never invokes a provider from a callback trampoline.
/// A full queue is observable through <see cref="TryEnqueue"/> so the session can complete the
/// native request with a transient failure instead of blocking a Cloud Files thread.
/// </remarks>
internal sealed class CloudProviderDispatcher : IAsyncDisposable
{
    private readonly Channel<CloudProviderWorkItem> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<CloudProviderRequestKind, SemaphoreSlim> _limits;
    private readonly Task[] _workers;
    private readonly object _activeGate = new();
    private readonly HashSet<CloudProviderWorkItem> _active = [];
    private int _accepting = 1;
    private int _disposed;
    private int _resourcesDisposed;

    internal CloudProviderDispatcher(CloudProviderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _queue = Channel.CreateBounded<CloudProviderWorkItem>(new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = false,
            AllowSynchronousContinuations = false,
        });
        _limits = new Dictionary<CloudProviderRequestKind, SemaphoreSlim>
        {
            [CloudProviderRequestKind.FetchData] = new(options.MaxConcurrentDataRequests),
            [CloudProviderRequestKind.FetchPlaceholders] = new(options.MaxConcurrentPlaceholderRequests),
            [CloudProviderRequestKind.ValidateData] = new(options.MaxConcurrentValidationRequests),
            [CloudProviderRequestKind.Dehydrate] = new(options.MaxConcurrentPolicyRequests),
            [CloudProviderRequestKind.Delete] = new(options.MaxConcurrentPolicyRequests),
            [CloudProviderRequestKind.Rename] = new(options.MaxConcurrentPolicyRequests),
            [CloudProviderRequestKind.CompletionNotification] = new(options.MaxConcurrentPolicyRequests),
        };
        _workers = Enumerable.Range(0, options.WorkerCount)
            .Select(_ => WorkerLoopAsync())
            .ToArray();
    }

    internal bool TryEnqueue(CloudProviderWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        if (Volatile.Read(ref _accepting) == 0 || Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        if (_queue.Writer.TryWrite(workItem))
        {
            return true;
        }

        workItem.Cancel();
        return false;
    }

    internal void StopAccepting() => Interlocked.Exchange(ref _accepting, 0);

    internal async ValueTask DisposeAsync(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopAccepting();
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        while (_queue.Reader.TryRead(out CloudProviderWorkItem? queued))
        {
            queued.Cancel();
        }

        CloudProviderWorkItem[] activeWork;
        lock (_activeGate)
        {
            activeWork = [.. _active];
        }

        foreach (CloudProviderWorkItem active in activeWork)
        {
            active.Cancel();
        }

        Task workers = Task.WhenAll(_workers);
        bool workersCompleted = false;
        try
        {
            await workers.WaitAsync(timeout).ConfigureAwait(false);
            workersCompleted = true;
        }
        catch (TimeoutException)
        {
            // Native disconnection is the final boundary for a provider that ignores cancellation.
        }
        finally
        {
            if (workersCompleted)
            {
                DisposeOwnedResources();
            }
            else
            {
                _ = DisposeOwnedResourcesAfterWorkersAsync(workers);
            }
        }
    }

    public ValueTask DisposeAsync() => DisposeAsync(TimeSpan.FromSeconds(30));

    private async Task WorkerLoopAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out CloudProviderWorkItem? workItem))
                {
                    await RunWorkItemAsync(workItem).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            while (_queue.Reader.TryRead(out CloudProviderWorkItem? queued))
            {
                queued.Cancel();
            }
        }
    }

    private async ValueTask RunWorkItemAsync(CloudProviderWorkItem workItem)
    {
        if (!_limits.TryGetValue(workItem.Kind, out SemaphoreSlim? limit))
        {
            workItem.Fail(new InvalidOperationException($"Unsupported provider request kind: {workItem.Kind}."));
            return;
        }

        bool registered = false;
        lock (_activeGate)
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _active.Add(workItem);
                registered = true;
            }
        }

        if (!registered)
        {
            workItem.Cancel();
            return;
        }

        bool acquired = false;
        try
        {
            await limit.WaitAsync(workItem.Cancellation.Token).ConfigureAwait(false);
            acquired = true;
            await workItem.RunAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (workItem.Cancellation.IsCancellationRequested)
        {
            workItem.Cancel();
        }
        catch (Exception exception)
        {
            workItem.Fail(exception);
        }
        finally
        {
            lock (_activeGate)
            {
                _active.Remove(workItem);
            }

            if (acquired)
            {
                limit.Release();
            }
        }
    }

    private async Task DisposeOwnedResourcesAfterWorkersAsync(Task workers)
    {
        try
        {
            await workers.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Work-item failures are translated at the work-item boundary. This catch protects
            // deferred cleanup if an unexpected worker failure reaches the aggregate task.
        }
        finally
        {
            DisposeOwnedResources();
        }
    }

    private void DisposeOwnedResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        _shutdown.Dispose();
        foreach (SemaphoreSlim limit in _limits.Values)
        {
            limit.Dispose();
        }
    }
}
