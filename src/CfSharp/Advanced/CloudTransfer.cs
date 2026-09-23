using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using CfSharp.Native;

namespace CfSharp;

/// <summary>Configures one provider-initiated transfer owned by a <see cref="CloudItemLease"/>.</summary>
public sealed record CloudTransferOptions
{
    /// <summary>Gets conservative transfer defaults.</summary>
    public static CloudTransferOptions Default { get; } = new();

    /// <summary>Gets an optional correlation vector copied into each operation.</summary>
    public CloudCorrelationVector? CorrelationVector { get; init; }

    /// <summary>Gets optional rich status details attached to each native operation.</summary>
    public CloudSyncStatus? OperationStatus { get; init; }

    /// <summary>Gets the maximum number of entries allowed in one placeholder batch.</summary>
    public int MaxBatchEntries { get; init; } = 128;

    internal void Validate()
    {
        if (MaxBatchEntries is <= 0 or > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxBatchEntries),
                MaxBatchEntries,
                "The transfer batch size must be between 1 and 4096 entries.");
        }

        if (OperationStatus is not null)
        {
            ArgumentNullException.ThrowIfNull(OperationStatus.Description);
            if (OperationStatus.Description.Contains('\0'))
            {
                throw new ArgumentException(
                    "The transfer status description cannot contain an embedded NUL.",
                    nameof(OperationStatus));
            }
        }
    }
}

/// <summary>Maps a managed transfer failure to a Cloud Files terminal NTSTATUS.</summary>
public enum CloudTransferFailure
{
    /// <summary>Generic provider failure.</summary>
    Unsuccessful = 0,

    /// <summary>The provider canceled the request.</summary>
    RequestCanceled = 1,

    /// <summary>The provider cannot reach its backing service.</summary>
    NetworkUnavailable = 2,

    /// <summary>The provider was asked to abort the request.</summary>
    RequestAborted = 3,
}

/// <summary>Returns the amount and terminal status retrieved from a placeholder.</summary>
public readonly record struct CloudTransferReadResult(int BytesRead, NtStatus CompletionStatus);

/// <summary>Describes the native result for one entry in a proactive placeholder transfer.</summary>
/// <param name="Index">Zero-based index in the submitted placeholder list.</param>
/// <param name="IsProcessed">Whether Windows reported that the entry was processed.</param>
/// <param name="NativeResult">The entry HRESULT, or <see langword="null"/> when unprocessed.</param>
public readonly record struct CloudTransferPlaceholderEntryResult(
    int Index,
    bool IsProcessed,
    int? NativeResult)
{
    /// <summary>Gets whether Windows processed this entry successfully.</summary>
    public bool IsSuccessful => IsProcessed && NativeResult is >= 0;
}

/// <summary>Returns structured per-entry results from a proactive placeholder transfer.</summary>
public sealed class CloudTransferPlaceholderBatchResult
{
    /// <summary>Initializes a result and takes a defensive copy of its entries.</summary>
    /// <param name="entries">One result for every submitted placeholder, in submission order.</param>
    public CloudTransferPlaceholderBatchResult(
        IReadOnlyList<CloudTransferPlaceholderEntryResult> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries.ToArray();
        if (Entries.Any(entry => entry.Index < 0 || entry.Index >= Entries.Count) ||
            Entries.Select(entry => entry.Index).Distinct().Count() != Entries.Count)
        {
            throw new ArgumentException(
                "Placeholder transfer result indexes must be unique and contiguous.",
                nameof(entries));
        }

        EntriesProcessed = Entries.Count(static entry => entry.IsProcessed);
    }

    /// <summary>Gets one result for every submitted placeholder, in submission order.</summary>
    public IReadOnlyList<CloudTransferPlaceholderEntryResult> Entries { get; }

    /// <summary>Gets the number of entries Windows reported as processed.</summary>
    public int EntriesProcessed { get; }

    /// <summary>Gets whether every submitted entry was processed.</summary>
    public bool IsComplete => EntriesProcessed == Entries.Count;

    /// <summary>Gets whether every processed entry completed successfully.</summary>
    public bool IsSuccessful => IsComplete && Entries.All(static entry => entry.IsSuccessful);
}

/// <summary>Owns one provider-initiated Cloud Files transfer key.</summary>
[SupportedOSPlatform("windows10.0.16299")]
public sealed unsafe class CloudTransfer : IDisposable, IAsyncDisposable
{
    private const long Alignment = 4096;
    private readonly CloudItemLease _lease;
    private readonly CloudTransferOptions _options;
    private readonly SafeCloudFilesProtectedHandle.CloudFilesHandleReference _handleReference;
    private readonly CfTransferKey _transferKey;
    private readonly CfConnectionKey _connectionKey;
    private readonly object _gate = new();
    private readonly List<TransferRange> _transferredRanges = [];
    private readonly Activity? _activity;
    private int _terminal;
    private int _disposed;

    internal CloudTransfer(
        CloudItemLease lease,
        CloudTransferOptions options,
        SafeCloudFilesProtectedHandle.CloudFilesHandleReference handleReference,
        CfTransferKey transferKey,
        CfConnectionKey connectionKey)
    {
        _lease = lease;
        _options = options;
        _handleReference = handleReference;
        _transferKey = transferKey;
        _connectionKey = connectionKey;
        _activity = CloudDiagnostics.StartActivity("cfsharp.item.transfer", "transfer");
        CloudDiagnostics.RecordTransferLifetime(created: true);
    }

    /// <summary>Gets the lease that owns the protected item lifetime.</summary>
    public CloudItemLease Lease => _lease;

    /// <summary>
    /// Transfers one aligned or end-of-file data range into the placeholder.
    /// </summary>
    /// <remarks>
    /// The owning lease must include write access. A successful range is retained by this transfer
    /// and cannot overlap a later data transfer; this gives retries a deterministic managed
    /// contract instead of relying on native range ordering.
    /// </remarks>
    public ValueTask TransferDataAsync(
        long offset,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        using IDisposable operation = EnterOperation();
        EnsureFileItem();
        EnsureWriteAccess();
        cancellationToken.ThrowIfCancellationRequested();
        if (data.Length == 0)
        {
            throw new ArgumentException("A transfer data buffer cannot be empty.", nameof(data));
        }

        long end = ValidateRange(offset, data.Length);
        EnsureRangeDoesNotOverlap(offset, end);
        CfCorrelationVector nativeVector = default;
        bool hasVector = _options.CorrelationVector is not null;
        if (hasVector)
        {
            nativeVector = _options.CorrelationVector!.Value.ToNative();
        }

        using SyncStatusBuffer? status = SyncStatusBuffer.Create(_options.OperationStatus);
        using MemoryHandle pinned = data.Pin();
        CfOperationInfo operationInfo = CreateOperationInfo(
            CfOperationType.TransferData,
            hasVector ? &nativeVector : null,
            status is null ? (CfSyncStatus*)null : status.Pointer);
        CfOperationParameters parameters = new()
        {
            ParamSize = checked((uint)(8 + sizeof(CfOperationTransferDataParameters))),
            TransferData = new CfOperationTransferDataParameters
            {
                CompletionStatus = NtStatus.Success,
                Buffer = pinned.Pointer,
                Offset = offset,
                Length = data.Length,
            },
        };

        int result = CfApi.CfExecute(&operationInfo, &parameters);
        ThrowIfFailed("CloudTransfer.TransferData", _lease.Item.FullPath, result);
        _transferredRanges.Add(new TransferRange(offset, end));
        return ValueTask.CompletedTask;
    }

    /// <summary>Completes the transfer with a documented Cloud Files failure status.</summary>
    /// <remarks>The owning lease must include write access; this is terminal for the transfer.</remarks>
    public ValueTask TransferDataFailureAsync(
        long offset,
        long length,
        CloudTransferFailure failure,
        CancellationToken cancellationToken = default)
    {
        using IDisposable operation = EnterOperation();
        EnsureFileItem();
        EnsureWriteAccess();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRange(offset, length);
        NtStatus statusValue = MapFailure(failure);
        CfCorrelationVector nativeVector = default;
        bool hasVector = _options.CorrelationVector is not null;
        if (hasVector)
        {
            nativeVector = _options.CorrelationVector!.Value.ToNative();
        }

        using SyncStatusBuffer? status = SyncStatusBuffer.Create(_options.OperationStatus);
        CfOperationInfo operationInfo = CreateOperationInfo(
            CfOperationType.TransferData,
            hasVector ? &nativeVector : null,
            status is null ? (CfSyncStatus*)null : status.Pointer);
        CfOperationParameters parameters = new()
        {
            ParamSize = checked((uint)(8 + sizeof(CfOperationTransferDataParameters))),
            TransferData = new CfOperationTransferDataParameters
            {
                CompletionStatus = statusValue,
                Buffer = null,
                Offset = offset,
                Length = length,
            },
        };

        int result = CfApi.CfExecute(&operationInfo, &parameters);
        ThrowIfFailed("CloudTransfer.TransferDataFailure", _lease.Item.FullPath, result);
        Interlocked.Exchange(ref _terminal, 1);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Acknowledges or rejects a previously transferred range when the sync root requires
    /// provider validation.
    /// </summary>
    /// <param name="offset">4 KiB-aligned beginning of the transferred range.</param>
    /// <param name="length">Transferred length, aligned unless it reaches the logical EOF.</param>
    /// <param name="failure">
    /// A Cloud Files failure to reject the range, or <see langword="null"/> to acknowledge it.
    /// </param>
    /// <param name="cancellationToken">Token checked before entering the native call.</param>
    /// <remarks>
    /// This operation is meaningful only for a sync root registered with
    /// <c>ValidationRequired</c>. The registration policy is owned by the sync root rather than
    /// the transfer object, so Windows remains the authority for rejecting calls on other roots.
    /// The owning lease must include write access and the range must have been successfully
    /// transferred by this object. The native call is synchronous; cancellation can prevent a call that has not started but
    /// cannot interrupt an unmanaged Cloud Files operation already in progress.
    /// </remarks>
    public ValueTask AcknowledgeDataAsync(
        long offset,
        long length,
        CloudTransferFailure? failure = null,
        CancellationToken cancellationToken = default)
    {
        using IDisposable operation = EnterOperation();
        EnsureFileItem();
        EnsureWriteAccess();
        cancellationToken.ThrowIfCancellationRequested();
        long end = ValidateRange(offset, length);
        EnsureRangeWasTransferred(offset, end);

        CfCorrelationVector nativeVector = default;
        bool hasVector = _options.CorrelationVector is not null;
        if (hasVector)
        {
            nativeVector = _options.CorrelationVector!.Value.ToNative();
        }

        using SyncStatusBuffer? status = SyncStatusBuffer.Create(_options.OperationStatus);
        CfOperationInfo operationInfo = CreateOperationInfo(
            CfOperationType.AckData,
            hasVector ? &nativeVector : null,
            status is null ? (CfSyncStatus*)null : status.Pointer);
        CfOperationParameters parameters = new()
        {
            ParamSize = checked((uint)(8 + sizeof(CfOperationAckDataParameters))),
            AckData = new CfOperationAckDataParameters
            {
                CompletionStatus = failure is null ? NtStatus.Success : MapFailure(failure.Value),
                Offset = offset,
                Length = length,
            },
        };

        int result = CfApi.CfExecute(&operationInfo, &parameters);
        ThrowIfFailed("CloudTransfer.AcknowledgeData", _lease.Item.FullPath, result);
        return ValueTask.CompletedTask;
    }

    /// <summary>Transfers a bounded page of child placeholders into a directory placeholder.</summary>
    /// <remarks>The owning directory lease must include write access.</remarks>
    public ValueTask<CloudTransferPlaceholderBatchResult> TransferPlaceholdersAsync(
        IReadOnlyList<CloudPlaceholderSpec> children,
        CloudPlaceholderBatchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using IDisposable operation = EnterOperation();
        EnsureWriteAccess();
        if (_lease.Item.Kind is not CloudItemKind.Directory)
        {
            throw new InvalidOperationException("Placeholder transfer requires a directory lease.");
        }

        ArgumentNullException.ThrowIfNull(children);
        if (children.Count == 0 || children.Count > _options.MaxBatchEntries)
        {
            throw new ArgumentOutOfRangeException(
                nameof(children),
                children.Count,
                $"The placeholder batch must contain 1 to {_options.MaxBatchEntries} entries.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        CloudPlaceholderBatchOptions selected = options ?? CloudPlaceholderBatchOptions.Default;
        CfPlaceholderCreateInfo[] nativeEntries = new CfPlaceholderCreateInfo[children.Count];
        List<GCHandle> pins = new(children.Count * 2);
        try
        {
            for (int index = 0; index < children.Count; index++)
            {
                CloudPlaceholderSpec child = children[index] ??
                    throw new ArgumentException("A placeholder batch cannot contain null entries.", nameof(children));
                char[] name = (child.Name + '\0').ToCharArray();
                byte[] identity = child.Identity.Encode();
                GCHandle namePin = GCHandle.Alloc(name, GCHandleType.Pinned);
                pins.Add(namePin);
                GCHandle identityPin = GCHandle.Alloc(identity, GCHandleType.Pinned);
                pins.Add(identityPin);
                nativeEntries[index] = new CfPlaceholderCreateInfo
                {
                    RelativeFileName = (char*)namePin.AddrOfPinnedObject(),
                    FsMetadata = CloudPlaceholderPlatform.CreateMetadata(child),
                    FileIdentity = (void*)identityPin.AddrOfPinnedObject(),
                    FileIdentityLength = checked((uint)identity.Length),
                    Flags = CloudPlaceholderPlatform.CreateFlags(child),
                };
            }

            CfCorrelationVector nativeVector = default;
            bool hasVector = _options.CorrelationVector is not null;
            if (hasVector)
            {
                nativeVector = _options.CorrelationVector!.Value.ToNative();
            }

            using SyncStatusBuffer? status = SyncStatusBuffer.Create(_options.OperationStatus);
            fixed (CfPlaceholderCreateInfo* entries = nativeEntries)
            {
                CfOperationInfo operationInfo = CreateOperationInfo(
                    CfOperationType.TransferPlaceholders,
                    hasVector ? &nativeVector : null,
                    status is null ? (CfSyncStatus*)null : status.Pointer);
                CfOperationParameters parameters = new()
                {
                    ParamSize = checked((uint)(8 + sizeof(CfOperationTransferPlaceholdersParameters))),
                    TransferPlaceholders = new CfOperationTransferPlaceholdersParameters
                    {
                        Flags = selected.StopOnFirstFailure
                            ? CfOperationTransferPlaceholdersFlags.StopOnError
                            : CfOperationTransferPlaceholdersFlags.None,
                        CompletionStatus = NtStatus.Success,
                        PlaceholderTotalCount = children.Count,
                        PlaceholderArray = entries,
                        PlaceholderCount = checked((uint)nativeEntries.Length),
                    },
                };

                int result = CfApi.CfExecute(&operationInfo, &parameters);
                ThrowIfFailed("CloudTransfer.TransferPlaceholders", _lease.Item.FullPath, result);
                uint processed = parameters.TransferPlaceholders.EntriesProcessed;
                if (processed > nativeEntries.Length)
                {
                    throw new InvalidDataException("Windows returned an invalid placeholder processed count.");
                }

                CloudTransferPlaceholderEntryResult[] results =
                    new CloudTransferPlaceholderEntryResult[nativeEntries.Length];
                for (int index = 0; index < nativeEntries.Length; index++)
                {
                    results[index] = index < processed
                        ? new CloudTransferPlaceholderEntryResult(
                            index,
                            IsProcessed: true,
                            NativeResult: nativeEntries[index].Result)
                        : new CloudTransferPlaceholderEntryResult(
                            index,
                            IsProcessed: false,
                            NativeResult: null);
                }

                return ValueTask.FromResult(new CloudTransferPlaceholderBatchResult(results));
            }
        }
        finally
        {
            for (int index = pins.Count - 1; index >= 0; index--)
            {
                pins[index].Free();
            }
        }

    }

    /// <summary>Retrieves previously transferred bytes from an aligned hydrated range.</summary>
    public ValueTask<CloudTransferReadResult> RetrieveDataAsync(
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        using IDisposable operation = EnterOperation();
        EnsureFileItem();
        cancellationToken.ThrowIfCancellationRequested();
        if (destination.Length == 0)
        {
            throw new ArgumentException("The destination buffer cannot be empty.", nameof(destination));
        }

        ValidateRange(offset, destination.Length);
        CfCorrelationVector nativeVector = default;
        bool hasVector = _options.CorrelationVector is not null;
        if (hasVector)
        {
            nativeVector = _options.CorrelationVector!.Value.ToNative();
        }

        using SyncStatusBuffer? status = SyncStatusBuffer.Create(_options.OperationStatus);
        using MemoryHandle pinned = destination.Pin();
        CfOperationInfo operationInfo = CreateOperationInfo(
            CfOperationType.RetrieveData,
            hasVector ? &nativeVector : null,
            status is null ? (CfSyncStatus*)null : status.Pointer);
        CfOperationParameters parameters = new()
        {
            ParamSize = checked((uint)(8 + sizeof(CfOperationRetrieveDataParameters))),
            RetrieveData = new CfOperationRetrieveDataParameters
            {
                Buffer = pinned.Pointer,
                Offset = offset,
                Length = destination.Length,
            },
        };

        int result = CfApi.CfExecute(&operationInfo, &parameters);
        ThrowIfFailed("CloudTransfer.RetrieveData", _lease.Item.FullPath, result);
        long returned = parameters.RetrieveData.ReturnedLength;
        if (returned < 0 || returned > destination.Length)
        {
            throw new InvalidDataException("Windows returned an invalid transfer length.");
        }

        return new ValueTask<CloudTransferReadResult>(
            new CloudTransferReadResult(checked((int)returned), NtStatus.Success));
    }

    /// <summary>Releases the transfer key and its protected-handle reference.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _terminal, 1);
            CfTransferKey key = _transferKey;
            CfApi.CfReleaseTransferKey(_handleReference.Win32Handle, &key);
            _handleReference.Dispose();
        }

        _lease.TransferDisposed(this);
        CloudDiagnostics.StopActivity(_activity, "disposed");
        CloudDiagnostics.RecordTransferLifetime(created: false);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private CfOperationInfo CreateOperationInfo(
        CfOperationType operationType,
        CfCorrelationVector* correlationVector,
        CfSyncStatus* syncStatus) => new()
        {
            StructSize = (uint)sizeof(CfOperationInfo),
            Type = operationType,
            ConnectionKey = _connectionKey,
            TransferKey = _transferKey,
            CorrelationVector = correlationVector,
            SyncStatus = syncStatus,
            RequestKey = new CfRequestKey { Internal = CfApi.DefaultRequestKey },
        };

    private long ValidateRange(long offset, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0);
        long end = checked(offset + length);
        if (offset % Alignment != 0)
        {
            throw new ArgumentException("Transfer offsets must be aligned to 4 KiB.", nameof(offset));
        }

        long logicalLength = 0;
        try
        {
            logicalLength = new FileInfo(_lease.Item.FullPath).Length;
        }
        catch (FileNotFoundException)
        {
            // The native call below reports the authoritative lifetime failure.
        }

        bool reachesEndOfFile = logicalLength > 0 && end >= logicalLength;
        if (length % Alignment != 0 && !reachesEndOfFile)
        {
            throw new ArgumentException(
                "Transfer lengths must be aligned to 4 KiB unless the range reaches the logical end of file.",
                nameof(length));
        }

        return end;
    }

    private void EnsureFileItem()
    {
        if (_lease.Item.Kind is not CloudItemKind.File)
        {
            throw new InvalidOperationException("Data transfer requires a file lease.");
        }
    }

    private void EnsureWriteAccess()
    {
        if (!_lease.Options.Access.HasFlag(CloudItemLeaseAccess.Write))
        {
            throw new InvalidOperationException(
                "The lease must include write access for this Cloud Files transfer operation.");
        }
    }

    private void EnsureRangeDoesNotOverlap(long offset, long end)
    {
        if (_transferredRanges.Any(range => offset < range.End && end > range.Offset))
        {
            throw new InvalidOperationException(
                "The transfer range overlaps a range already accepted by this transfer.");
        }
    }

    private void EnsureRangeWasTransferred(long offset, long end)
    {
        if (!_transferredRanges.Any(range => offset >= range.Offset && end <= range.End))
        {
            throw new InvalidOperationException(
                "The acknowledged range was not previously accepted by this transfer.");
        }
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _terminal) != 0)
        {
            throw new InvalidOperationException("The Cloud Files transfer has already reached a terminal state.");
        }

        if (_lease.IsInvalidated)
        {
            throw new InvalidOperationException("The protected item lease has been invalidated.");
        }
    }

    private OperationLease EnterOperation()
    {
        Monitor.Enter(_gate);
        try
        {
            EnsureUsable();
            return new OperationLease(_gate);
        }
        catch
        {
            Monitor.Exit(_gate);
            throw;
        }
    }

    private sealed class OperationLease : IDisposable
    {
        private object? _gate;

        internal OperationLease(object gate) => _gate = gate;

        public void Dispose()
        {
            object? gate = Interlocked.Exchange(ref _gate, null);
            if (gate is not null)
            {
                Monitor.Exit(gate);
            }
        }
    }

    private readonly record struct TransferRange(long Offset, long End);

    private static NtStatus MapFailure(CloudTransferFailure failure) => failure switch
    {
        CloudTransferFailure.Unsuccessful => NtStatus.CloudFileUnsuccessful,
        CloudTransferFailure.RequestCanceled => NtStatus.CloudFileRequestCanceled,
        CloudTransferFailure.NetworkUnavailable => NtStatus.CloudFileNetworkUnavailable,
        CloudTransferFailure.RequestAborted => NtStatus.CloudFileRequestAborted,
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, "The transfer failure is not defined."),
    };

    private static void ThrowIfFailed(string operation, string path, int hresult)
    {
        if (hresult < 0)
        {
            CloudDiagnostics.RecordNativeFailure(operation);
            throw CloudFilesException.FromHResult(operation, path, hresult);
        }
    }

    private sealed unsafe class SyncStatusBuffer : IDisposable
    {
        private GCHandle _pin;
        private byte[]? _buffer;

        private SyncStatusBuffer(CloudSyncStatus status)
        {
            byte[] description = System.Text.Encoding.Unicode.GetBytes(status.Description + '\0');
            byte[] deviceId = status.DeviceId.ToArray();
            int header = Marshal.SizeOf<CfSyncStatus>();
            int deviceOffset = checked(header + description.Length);
            _buffer = new byte[checked(deviceOffset + deviceId.Length)];
            Buffer.BlockCopy(description, 0, _buffer, header, description.Length);
            Buffer.BlockCopy(deviceId, 0, _buffer, deviceOffset, deviceId.Length);
            _pin = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            Pointer->StructSize = checked((uint)_buffer.Length);
            Pointer->Code = status.Code;
            Pointer->DescriptionOffset = checked((uint)header);
            Pointer->DescriptionLength = checked((uint)description.Length);
            Pointer->DeviceIdOffset = checked((uint)deviceOffset);
            Pointer->DeviceIdLength = checked((uint)deviceId.Length);
        }

        internal CfSyncStatus* Pointer => (CfSyncStatus*)_pin.AddrOfPinnedObject();

        internal static SyncStatusBuffer? Create(CloudSyncStatus? status) =>
            status is null ? null : new SyncStatusBuffer(status);

        public void Dispose()
        {
            if (_pin.IsAllocated)
            {
                _pin.Free();
            }

            _buffer = null;
        }
    }
}
