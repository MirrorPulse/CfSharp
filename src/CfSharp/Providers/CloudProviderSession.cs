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
/// connection key. Create it with <see cref="Connect(CloudSyncRoot, ICloudFileContentProvider)"/>
/// and dispose it deterministically before
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
    private const int CallbackRegistrationCount = 5;

    private readonly ICloudFileContentProvider _contentProvider;
    private readonly CloudProviderSessionOptions _options;
    private readonly object _lifecycleGate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentDictionary<long, ActiveRequest> _requests = new();
    private readonly ConcurrentDictionary<long, PlaceholderRequest> _placeholderRequests = new();
    private readonly ConcurrentDictionary<string, string> _directoryContinuations = new(
        StringComparer.OrdinalIgnoreCase);
    private CloudProviderDispatcher? _dispatcher;
    private unsafe CfCallbackRegistration* _callbackTable;
    private GCHandle _callbackContext;
    private CfConnectionKey _connectionKey;
    private int _stopping;
    private int _disposed;

    private CloudProviderSession(
        ICloudFileContentProvider contentProvider,
        CloudProviderSessionOptions options)
    {
        _contentProvider = contentProvider;
        _options = options;
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
        => Connect(syncRoot, contentProvider, CloudProviderSessionOptions.Default);

    /// <summary>Connects a content provider with an explicit bounded runtime configuration.</summary>
    /// <param name="syncRoot">Registered root that receives hydration callbacks.</param>
    /// <param name="contentProvider">Thread-safe source of complete logical file streams.</param>
    /// <param name="options">Immutable queue, concurrency, transfer, and shutdown limits.</param>
    /// <returns>An owning session that must be disposed before the root is unregistered.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is outside its supported bound.</exception>
    public static CloudProviderSession Connect(
        CloudSyncRoot syncRoot,
        ICloudFileContentProvider contentProvider,
        CloudProviderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(syncRoot);
        ArgumentNullException.ThrowIfNull(contentProvider);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        CloudProviderSession session = new(contentProvider, options);
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

        lock (_lifecycleGate)
        {
            Volatile.Write(ref _stopping, 1);
            _shutdown.Cancel();
            foreach (ActiveRequest request in _requests.Values)
            {
                request.Cancel();
            }

            foreach (PlaceholderRequest request in _placeholderRequests.Values)
            {
                request.Cancel();
            }
        }

        try
        {
            if (_dispatcher is not null)
            {
                await _dispatcher.DisposeAsync(_options.ShutdownTimeout).ConfigureAwait(false);
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
        _dispatcher = new CloudProviderDispatcher(_options);
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
            Type = CfCallbackType.FetchPlaceholders,
            Callback = &FetchPlaceholdersCallback,
        };
        _callbackTable[3] = new CfCallbackRegistration
        {
            Type = CfCallbackType.CancelFetchPlaceholders,
            Callback = &CancelFetchPlaceholdersCallback,
        };
        _callbackTable[4] = new CfCallbackRegistration
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
                CreateConnectFlags(_options),
                out _connectionKey);
        }

        if (result < 0)
        {
            ReleaseNativeState();
            ThrowIfFailed("CloudProviderSession.Connect", result);
        }
    }

    private static CfConnectFlags CreateConnectFlags(CloudProviderSessionOptions options)
    {
        CfConnectFlags flags = CfConnectFlags.None;
        if (options.RequireFullFilePath)
        {
            flags |= CfConnectFlags.RequireFullFilePath;
        }

        if (options.RequireProcessInfo)
        {
            flags |= CfConnectFlags.RequireProcessInfo;
        }

        if (options.BlockSelfImplicitHydration)
        {
            flags |= CfConnectFlags.BlockSelfImplicitHydration;
        }

        return flags;
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
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _stopping) != 0)
            {
                CompleteRequest(activeRequest, NtStatus.CloudFileRequestAborted);
                return;
            }

            if (!_requests.TryAdd(requestKey, activeRequest))
            {
                CompleteRequest(activeRequest, NtStatus.CloudFileUnsuccessful);
                return;
            }

            CloudProviderWorkItem workItem = new(
                CloudProviderRequestKind.FetchData,
                cancellation,
                _ => new ValueTask(ProcessRequestAsync(activeRequest)),
                () => CompleteRequest(activeRequest, NtStatus.CloudFileRequestAborted),
                _ => CompleteRequest(activeRequest, NtStatus.CloudFileUnsuccessful));
            if (_dispatcher is null || !_dispatcher.TryEnqueue(workItem))
            {
                CompleteRequest(activeRequest, NtStatus.CloudFileUnsuccessful);
            }
        }
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

            if (activeRequest.Request.Offset < 0 ||
                activeRequest.Request.Length <= 0 ||
                activeRequest.Request.Offset > activeRequest.Request.FileSize ||
                activeRequest.Request.Length > activeRequest.Request.FileSize - activeRequest.Request.Offset)
            {
                throw new InvalidDataException("The requested range exceeds the logical file size.");
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(
                Math.Min(_options.TransferChunkSize, checked((int)activeRequest.Request.Length)));
            try
            {
                long offset = activeRequest.Request.Offset;
                long remaining = activeRequest.Request.Length;
                while (remaining != 0)
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
        finally
        {
            RemoveRequest(activeRequest);
        }
    }

    private unsafe void DispatchFetchPlaceholders(
        CfCallbackInfo* callbackInfo,
        CfCallbackParameters* parameters)
    {
        int identityLength = checked((int)callbackInfo->FileIdentityLength);
        byte[] identity = identityLength == 0
            ? []
            : new ReadOnlySpan<byte>(callbackInfo->FileIdentity, identityLength).ToArray();
        string path = callbackInfo->NormalizedPath is null
            ? string.Empty
            : new string(callbackInfo->NormalizedPath);
        string pattern = parameters->FetchPlaceholders.Pattern is null
            ? string.Empty
            : new string(parameters->FetchPlaceholders.Pattern);
        _directoryContinuations.TryGetValue(path, out string? continuationToken);
        CloudProviderFetchPlaceholdersRequest request = new(
            path,
            identity,
            pattern,
            continuationToken);
        CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        PlaceholderRequest activeRequest = new(
            callbackInfo->ConnectionKey,
            callbackInfo->TransferKey,
            callbackInfo->RequestKey,
            request,
            cancellation);

        long requestKey = callbackInfo->RequestKey.Internal;
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _stopping) != 0)
            {
                CompletePlaceholderRequest(activeRequest, NtStatus.CloudFileRequestAborted);
                return;
            }

            if (!_placeholderRequests.TryAdd(requestKey, activeRequest))
            {
                CompletePlaceholderRequest(activeRequest, NtStatus.CloudFileUnsuccessful);
                return;
            }

            CloudProviderWorkItem workItem = new(
                CloudProviderRequestKind.FetchPlaceholders,
                cancellation,
                _ => new ValueTask(ProcessPlaceholdersAsync(activeRequest)),
                () => CompletePlaceholderRequest(activeRequest, NtStatus.CloudFileRequestAborted),
                _ => CompletePlaceholderRequest(activeRequest, NtStatus.CloudFileUnsuccessful));
            if (_dispatcher is null || !_dispatcher.TryEnqueue(workItem))
            {
                CompletePlaceholderRequest(activeRequest, NtStatus.CloudFileUnsuccessful);
            }
        }
    }

    private async Task ProcessPlaceholdersAsync(PlaceholderRequest activeRequest)
    {
        try
        {
            if (_contentProvider is not ICloudDemandProvider demandProvider)
            {
                throw new NotSupportedException("The provider does not support directory population.");
            }

            CloudProviderDirectoryPage page = await demandProvider
                .FetchChildrenAsync(activeRequest.Request, activeRequest.Cancellation.Token)
                .ConfigureAwait(false);
            SendPlaceholders(activeRequest, page);
            if (page.ContinuationToken is null)
            {
                _directoryContinuations.TryRemove(activeRequest.Request.NormalizedPath, out _);
            }
            else
            {
                _directoryContinuations[activeRequest.Request.NormalizedPath] = page.ContinuationToken;
            }
        }
        catch (OperationCanceledException)
        {
            SendPlaceholderFailure(activeRequest, NtStatus.CloudFileRequestCanceled);
        }
        catch (Exception)
        {
            SendPlaceholderFailure(activeRequest, NtStatus.CloudFileUnsuccessful);
        }
        finally
        {
            RemovePlaceholderRequest(activeRequest);
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

    private static unsafe void SendPlaceholders(
        PlaceholderRequest request,
        CloudProviderDirectoryPage page)
    {
        CloudPlaceholderSpec[] specifications = page.Children.ToArray();
        CfPlaceholderCreateInfo[] entries = new CfPlaceholderCreateInfo[specifications.Length];
        List<GCHandle> pinned = new(specifications.Length * 2);
        try
        {
            for (int index = 0; index < specifications.Length; index++)
            {
                CloudPlaceholderSpec specification = specifications[index];
                char[] name = (specification.Name + '\0').ToCharArray();
                byte[] identity = specification.Identity.Encode();
                GCHandle nameHandle = GCHandle.Alloc(name, GCHandleType.Pinned);
                pinned.Add(nameHandle);
                GCHandle identityHandle = GCHandle.Alloc(identity, GCHandleType.Pinned);
                pinned.Add(identityHandle);
                entries[index] = new CfPlaceholderCreateInfo
                {
                    RelativeFileName = (char*)nameHandle.AddrOfPinnedObject(),
                    FsMetadata = CloudPlaceholderPlatform.CreateMetadata(specification),
                    FileIdentity = (void*)identityHandle.AddrOfPinnedObject(),
                    FileIdentityLength = checked((uint)identity.Length),
                    Flags = CloudPlaceholderPlatform.CreateFlags(specification),
                };
            }

            if (!request.TryMarkTerminal())
            {
                return;
            }

            fixed (CfPlaceholderCreateInfo* entriesPointer = entries)
            {
                CfOperationInfo operationInfo = request.CreateOperationInfo(
                    CfOperationType.TransferPlaceholders);
                CfOperationParameters parameters = default;
                parameters.ParamSize = checked((uint)(8 + sizeof(CfOperationTransferPlaceholdersParameters)));
                parameters.TransferPlaceholders = new CfOperationTransferPlaceholdersParameters
                {
                    Flags = page.IsComplete &&
                        (page.TotalCount is null || page.TotalCount <= entries.Length)
                        ? CfOperationTransferPlaceholdersFlags.DisableOnDemandPopulation
                        : CfOperationTransferPlaceholdersFlags.None,
                    CompletionStatus = NtStatus.Success,
                    PlaceholderTotalCount = page.TotalCount ?? -1,
                    PlaceholderArray = entriesPointer,
                    PlaceholderCount = checked((uint)entries.Length),
                };
                int result = CfApi.CfExecute(&operationInfo, &parameters);
                ThrowIfFailed("CloudProviderSession.TransferPlaceholders", result);
            }
        }
        finally
        {
            for (int index = pinned.Count - 1; index >= 0; index--)
            {
                pinned[index].Free();
            }
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

    private static unsafe void SendPlaceholderFailure(
        PlaceholderRequest request,
        NtStatus status)
    {
        if (!request.TryMarkTerminal())
        {
            return;
        }

        CfOperationInfo operationInfo = request.CreateOperationInfo(
            CfOperationType.TransferPlaceholders);
        CfOperationParameters parameters = default;
        parameters.ParamSize = checked((uint)(8 + sizeof(CfOperationTransferPlaceholdersParameters)));
        parameters.TransferPlaceholders = new CfOperationTransferPlaceholdersParameters
        {
            CompletionStatus = status,
            PlaceholderTotalCount = 0,
            PlaceholderArray = null,
            PlaceholderCount = 0,
        };
        _ = CfApi.CfExecute(&operationInfo, &parameters);
    }

    private void CompleteRequest(ActiveRequest activeRequest, NtStatus status)
    {
        SendFailure(activeRequest, status);
        RemoveRequest(activeRequest);
    }

    private void RemoveRequest(ActiveRequest activeRequest)
    {
        ((ICollection<KeyValuePair<long, ActiveRequest>>)_requests).Remove(
            new KeyValuePair<long, ActiveRequest>(
                activeRequest.RequestKey.Internal,
                activeRequest));
        activeRequest.DisposeCancellation();
    }

    private void CompletePlaceholderRequest(PlaceholderRequest activeRequest, NtStatus status)
    {
        SendPlaceholderFailure(activeRequest, status);
        RemovePlaceholderRequest(activeRequest);
    }

    private void RemovePlaceholderRequest(PlaceholderRequest activeRequest)
    {
        ((ICollection<KeyValuePair<long, PlaceholderRequest>>)_placeholderRequests).Remove(
            new KeyValuePair<long, PlaceholderRequest>(
                activeRequest.RequestKey.Internal,
                activeRequest));
        activeRequest.DisposeCancellation();
    }

    private unsafe void CancelRequest(CfCallbackInfo* callbackInfo)
    {
        if (_requests.TryGetValue(callbackInfo->RequestKey.Internal, out ActiveRequest? request))
        {
            request.Cancel();
        }

        if (_placeholderRequests.TryGetValue(
            callbackInfo->RequestKey.Internal,
            out PlaceholderRequest? placeholderRequest))
        {
            placeholderRequest.Cancel();
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

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static unsafe void FetchPlaceholdersCallback(
        CfCallbackInfo* callbackInfo,
        CfCallbackParameters* callbackParameters)
    {
        try
        {
            if (callbackInfo is not null && callbackParameters is not null)
            {
                GetSession(callbackInfo)?.DispatchFetchPlaceholders(callbackInfo, callbackParameters);
            }
        }
        catch (Exception)
        {
            // Exceptions must never cross the unmanaged callback boundary.
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static unsafe void CancelFetchPlaceholdersCallback(
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
        private int _cancellationDisposed;

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

        internal void DisposeCancellation()
        {
            if (Interlocked.Exchange(ref _cancellationDisposed, 1) == 0)
            {
                Cancellation.Dispose();
            }
        }
    }

    private sealed class PlaceholderRequest
    {
        private int _terminal;
        private int _cancellationDisposed;

        internal PlaceholderRequest(
            CfConnectionKey connectionKey,
            CfTransferKey transferKey,
            CfRequestKey requestKey,
            CloudProviderFetchPlaceholdersRequest request,
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

        internal CloudProviderFetchPlaceholdersRequest Request { get; }

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

        internal unsafe CfOperationInfo CreateOperationInfo(CfOperationType type) => new()
        {
            StructSize = (uint)sizeof(CfOperationInfo),
            Type = type,
            ConnectionKey = ConnectionKey,
            TransferKey = TransferKey,
            RequestKey = RequestKey,
        };

        internal bool TryMarkTerminal() => Interlocked.Exchange(ref _terminal, 1) == 0;

        internal void DisposeCancellation()
        {
            if (Interlocked.Exchange(ref _cancellationDisposed, 1) == 0)
            {
                Cancellation.Dispose();
            }
        }
    }
}
