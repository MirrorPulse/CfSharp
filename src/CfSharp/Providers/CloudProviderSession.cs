using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using CfSharp.Native;

namespace CfSharp;

/// <summary>Owns a process-scoped provider connection that hydrates file placeholders.</summary>
/// <remarks>
/// <para>
/// A session owns its native callback table, callback context, cancellation registry, and
/// connection key. Create it with <see cref="Connect"/> and dispose it deterministically before
/// unregistering the sync root. One session may serve concurrent Windows requests.
/// </para>
/// <para>
/// Disposal stops new provider dispatch, cancels active requests, drains cooperative handlers,
/// disconnects from Windows, and only then releases callback memory. The persistent sync-root
/// registration is not removed.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.16299")]
public sealed class CloudProviderSession : IDisposable, IAsyncDisposable
{
    private const int CallbackRegistrationCount = 3;
    private const int TransferBufferSize = 64 * 1024;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);

    private readonly ICloudFileContentProvider _contentProvider;
    private readonly object _lifecycleGate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<long, ActiveRequest> _requests = new();
    private readonly ConcurrentDictionary<long, Task> _tasks = new();
    private unsafe CfCallbackRegistration* _callbackTable;
    private GCHandle _callbackContext;
    private CfConnectionKey _connectionKey;
    private long _nextTaskId;
    private int _stopping;
    private int _disposed;

    private CloudProviderSession(ICloudFileContentProvider contentProvider)
    {
        _contentProvider = contentProvider;
    }

    /// <summary>Connects a content provider to a persistently registered sync root.</summary>
    /// <param name="syncRoot">Registered root that receives hydration callbacks.</param>
    /// <param name="contentProvider">Thread-safe source of complete logical file streams.</param>
    /// <returns>An owning session that must be disposed before the root is unregistered.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="PlatformNotSupportedException">The Cloud Files API is unavailable.</exception>
    /// <exception cref="CloudFilesException">Windows rejects the provider connection.</exception>
    public static CloudProviderSession Connect(
        CloudSyncRoot syncRoot,
        ICloudFileContentProvider contentProvider)
    {
        ArgumentNullException.ThrowIfNull(syncRoot);
        ArgumentNullException.ThrowIfNull(contentProvider);

        CloudProviderSession session = new(contentProvider);
        session.ConnectCore(syncRoot.Path);
        return session;
    }

    /// <summary>Gets the native connection status reported by Windows.</summary>
    /// <returns>A fresh status value for this connection.</returns>
    /// <exception cref="ObjectDisposedException">The session is stopping or disposed.</exception>
    /// <exception cref="CloudFilesException">Windows cannot query the connection.</exception>
    public CloudProviderStatus GetStatus()
    {
        ThrowIfStopping();
        int result = CfApi.CfQuerySyncProviderStatus(_connectionKey, out CfSyncProviderStatus status);
        ThrowIfFailed("CloudProviderSession.GetStatus", result);
        return (CloudProviderStatus)status;
    }

    /// <summary>Synchronously stops the provider session and releases its callback resources.</summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    /// <summary>Stops the provider session and asynchronously drains cooperative handlers.</summary>
    /// <returns>A task that completes after disconnection and native-memory release.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Task[] tasks;
        lock (_lifecycleGate)
        {
            Volatile.Write(ref _stopping, 1);
            _shutdown.Cancel();
            foreach (ActiveRequest request in _requests.Values)
            {
                request.Cancel();
            }

            tasks = _tasks.Values.ToArray();
        }

        try
        {
            if (tasks.Length > 0)
            {
                try
                {
                    await Task.WhenAll(tasks).WaitAsync(ShutdownTimeout).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Disconnection below is the final boundary for a handler that ignored cancellation.
                }
                catch (Exception)
                {
                    // Request failures are already translated into native terminal statuses.
                }
            }
        }
        finally
        {
            int disconnectResult = CfApi.CfDisconnectSyncRoot(_connectionKey);
            ReleaseNativeState();
            _shutdown.Dispose();
            GC.SuppressFinalize(this);
            ThrowIfFailed("CloudProviderSession.Dispose", disconnectResult);
        }
    }

    private unsafe void ConnectCore(string path)
    {
        _callbackTable = (CfCallbackRegistration*)NativeMemory.Alloc(
            (nuint)CallbackRegistrationCount,
            (nuint)sizeof(CfCallbackRegistration));
        if (_callbackTable is null)
        {
            throw new InvalidOperationException(
                "Unable to allocate the Cloud Files callback table.");
        }

        _callbackTable[0] = new CfCallbackRegistration
        {
            Type = CfCallbackType.FetchData,
            Callback = &FetchDataCallback,
        };
        _callbackTable[1] = new CfCallbackRegistration
        {
            Type = CfCallbackType.CancelFetchData,
            Callback = &CancelFetchDataCallback,
        };
        _callbackTable[2] = new CfCallbackRegistration
        {
            Type = CfCallbackType.None,
            Callback = null,
        };
        _callbackContext = GCHandle.Alloc(this, GCHandleType.Normal);

        int result;
        fixed (char* pathPointer = path)
        {
            result = CfApi.CfConnectSyncRoot(
                pathPointer,
                _callbackTable,
                (void*)GCHandle.ToIntPtr(_callbackContext),
                CfConnectFlags.None,
                out _connectionKey);
        }

        if (result < 0)
        {
            ReleaseNativeState();
            ThrowIfFailed("CloudProviderSession.Connect", result);
        }
    }

    private unsafe void DispatchFetch(
        CfCallbackInfo* callbackInfo,
        CfCallbackParameters* parameters)
    {
        CfCallbackFetchDataParameters nativeRequest = parameters->FetchData;
        int identityLength = checked((int)callbackInfo->FileIdentityLength);
        byte[] identity = identityLength == 0
            ? []
            : new ReadOnlySpan<byte>(callbackInfo->FileIdentity, identityLength).ToArray();
        string path = callbackInfo->NormalizedPath is null
            ? string.Empty
            : new string(callbackInfo->NormalizedPath);
        CloudFileFetchRequest request = new(
            path,
            identity,
            callbackInfo->FileSize,
            nativeRequest.RequiredFileOffset,
            nativeRequest.RequiredLength);
        CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        ActiveRequest activeRequest = new(
            callbackInfo->ConnectionKey,
            callbackInfo->TransferKey,
            callbackInfo->RequestKey,
            request,
            cancellation);

        long requestKey = callbackInfo->RequestKey.Internal;
        long taskId;
        Task task;
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _stopping) != 0)
            {
                SendFailure(activeRequest, NtStatus.CloudFileRequestAborted);
                cancellation.Dispose();
                return;
            }

            if (!_requests.TryAdd(requestKey, activeRequest))
            {
                SendFailure(activeRequest, NtStatus.CloudFileUnsuccessful);
                cancellation.Dispose();
                return;
            }

            taskId = Interlocked.Increment(ref _nextTaskId);
            task = Task.Run(() => ProcessRequestAsync(activeRequest));
            _tasks[taskId] = task;
        }

        _ = ObserveRequestAsync(taskId, requestKey, activeRequest, task);
    }

    private async Task ProcessRequestAsync(ActiveRequest activeRequest)
    {
        try
        {
            CancellationToken cancellationToken = activeRequest.Cancellation.Token;
            await using Stream source = await _contentProvider
                .OpenReadAsync(activeRequest.Request, cancellationToken)
                .ConfigureAwait(false);
            if (!source.CanRead)
            {
                throw new InvalidOperationException("The content provider returned an unreadable stream.");
            }

            if (activeRequest.Request.Offset != 0)
            {
                if (!source.CanSeek)
                {
                    throw new InvalidOperationException(
                        "A non-seekable content stream cannot satisfy a non-zero file offset.");
                }

                source.Seek(activeRequest.Request.Offset, SeekOrigin.Begin);
            }

            long availableLength = Math.Max(
                0,
                activeRequest.Request.FileSize - activeRequest.Request.Offset);
            long remaining = Math.Min(activeRequest.Request.Length, availableLength);
            if (remaining <= 0)
            {
                throw new EndOfStreamException("The requested range contains no transferable bytes.");
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(
                checked((int)Math.Min(TransferBufferSize, remaining)));
            try
            {
                long offset = activeRequest.Request.Offset;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int requested = checked((int)Math.Min(buffer.Length, remaining));
                    int read = await ReadExactlyAsync(
                        source,
                        buffer.AsMemory(0, requested),
                        cancellationToken).ConfigureAwait(false);
                    SendData(activeRequest, buffer.AsSpan(0, read), offset);
                    offset += read;
                    remaining -= read;
                }

                activeRequest.MarkSuccessful();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            SendFailure(activeRequest, NtStatus.CloudFileRequestCanceled);
        }
        catch (Exception)
        {
            SendFailure(activeRequest, NtStatus.CloudFileUnsuccessful);
        }
    }

    private static async ValueTask<int> ReadExactlyAsync(
        Stream source,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = await source.ReadAsync(destination[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("The content source ended before the requested range.");
            }

            total += read;
        }

        return total;
    }

    private static unsafe void SendData(
        ActiveRequest request,
        ReadOnlySpan<byte> data,
        long offset)
    {
        fixed (byte* buffer = data)
        {
            CfOperationInfo operationInfo = request.CreateOperationInfo();
            CfOperationParameters parameters = default;
            parameters.ParamSize = checked((uint)(8 + sizeof(CfOperationTransferDataParameters)));
            parameters.TransferData = new CfOperationTransferDataParameters
            {
                CompletionStatus = NtStatus.Success,
                Buffer = buffer,
                Offset = offset,
                Length = data.Length,
            };

            int result = CfApi.CfExecute(&operationInfo, &parameters);
            ThrowIfFailed("CloudProviderSession.TransferData", result);
        }
    }

    private static unsafe void SendFailure(ActiveRequest request, NtStatus status)
    {
        if (!request.TryMarkTerminal())
        {
            return;
        }

        CfOperationInfo operationInfo = request.CreateOperationInfo();
        CfOperationParameters parameters = default;
        parameters.ParamSize = checked((uint)(8 + sizeof(CfOperationTransferDataParameters)));
        parameters.TransferData = new CfOperationTransferDataParameters
        {
            CompletionStatus = status,
            Buffer = null,
            Offset = request.Request.Offset,
            Length = request.Request.Length,
        };
        _ = CfApi.CfExecute(&operationInfo, &parameters);
    }

    private async Task ObserveRequestAsync(
        long taskId,
        long requestKey,
        ActiveRequest activeRequest,
        Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            _tasks.TryRemove(taskId, out _);
            _requests.TryRemove(requestKey, out _);
            activeRequest.Cancellation.Dispose();
        }
    }

    private unsafe void CancelRequest(CfCallbackInfo* callbackInfo)
    {
        if (_requests.TryGetValue(callbackInfo->RequestKey.Internal, out ActiveRequest? request))
        {
            request.Cancel();
        }
    }

    private static unsafe CloudProviderSession? GetSession(CfCallbackInfo* callbackInfo)
    {
        if (callbackInfo is null || callbackInfo->CallbackContext is null)
        {
            return null;
        }

        return GCHandle.FromIntPtr((nint)callbackInfo->CallbackContext).Target
            as CloudProviderSession;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static unsafe void FetchDataCallback(
        CfCallbackInfo* callbackInfo,
        CfCallbackParameters* callbackParameters)
    {
        try
        {
            if (callbackInfo is not null && callbackParameters is not null)
            {
                GetSession(callbackInfo)?.DispatchFetch(callbackInfo, callbackParameters);
            }
        }
        catch (Exception)
        {
            // Exceptions must never cross the unmanaged callback boundary.
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static unsafe void CancelFetchDataCallback(
        CfCallbackInfo* callbackInfo,
        CfCallbackParameters* callbackParameters)
    {
        _ = callbackParameters;
        try
        {
            if (callbackInfo is not null)
            {
                GetSession(callbackInfo)?.CancelRequest(callbackInfo);
            }
        }
        catch (Exception)
        {
            // Exceptions must never cross the unmanaged callback boundary.
        }
    }

    private unsafe void ReleaseNativeState()
    {
        if (_callbackContext.IsAllocated)
        {
            _callbackContext.Free();
        }

        if (_callbackTable is not null)
        {
            NativeMemory.Free(_callbackTable);
            _callbackTable = null;
        }
    }

    private void ThrowIfStopping()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
    }

    private static void ThrowIfFailed(string operation, int hresult)
    {
        if (hresult < 0)
        {
            throw CloudFilesException.FromHResult(operation, hresult);
        }
    }

    private sealed class ActiveRequest
    {
        private int _terminal;

        internal ActiveRequest(
            CfConnectionKey connectionKey,
            CfTransferKey transferKey,
            CfRequestKey requestKey,
            CloudFileFetchRequest request,
            CancellationTokenSource cancellation)
        {
            ConnectionKey = connectionKey;
            TransferKey = transferKey;
            RequestKey = requestKey;
            Request = request;
            Cancellation = cancellation;
        }

        internal CfConnectionKey ConnectionKey { get; }

        internal CfTransferKey TransferKey { get; }

        internal CfRequestKey RequestKey { get; }

        internal CloudFileFetchRequest Request { get; }

        internal CancellationTokenSource Cancellation { get; }

        internal void Cancel()
        {
            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Completion won the race and already released this request token.
            }
        }

        internal unsafe CfOperationInfo CreateOperationInfo() => new()
        {
            StructSize = (uint)sizeof(CfOperationInfo),
            Type = CfOperationType.TransferData,
            ConnectionKey = ConnectionKey,
            TransferKey = TransferKey,
            RequestKey = RequestKey,
        };

        internal void MarkSuccessful() => Interlocked.Exchange(ref _terminal, 1);

        internal bool TryMarkTerminal() => Interlocked.Exchange(ref _terminal, 1) == 0;
    }
}
