using System.Runtime.Versioning;

using CfSharp.Native;

namespace CfSharp;

/// <summary>Owns one protected Cloud Files item lifetime without exposing a raw handle.</summary>
[SupportedOSPlatform("windows10.0.16299")]
public sealed unsafe class CloudItemLease : IDisposable, IAsyncDisposable
{
    private readonly CloudFileSystem.CloudFileSystemOperationLease _operation;
    private readonly CloudProviderSession _providerSession;
    private readonly SafeCloudFilesProtectedHandle _protectedHandle;
    private readonly object _gate = new();
    private readonly CloudItemLeaseOptions _options;
    private CloudTransfer? _transfer;
    private int _disposed;

    internal CloudItemLease(
        CloudItem item,
        CloudItemLeaseOptions options,
        CloudFileSystem.CloudFileSystemOperationLease operation,
        CloudProviderSession providerSession,
        SafeCloudFilesProtectedHandle protectedHandle)
    {
        Item = item;
        _options = options;
        _operation = operation;
        _providerSession = providerSession;
        _protectedHandle = protectedHandle;
    }

    /// <summary>Gets the immutable item reference held by this lease.</summary>
    public CloudItem Item { get; }

    /// <summary>Gets the validated access options used to open the lease.</summary>
    public CloudItemLeaseOptions Options => _options;

    /// <summary>Gets whether Windows has closed or invalidated the protected handle.</summary>
    public bool IsInvalidated => _protectedHandle.IsClosed || _protectedHandle.IsInvalid;

    /// <summary>Reads the current native correlation vector, copying it before returning.</summary>
    /// <returns>The vector, or null when Windows has not assigned one.</returns>
    public CloudCorrelationVector? GetCorrelationVector()
    {
        EnsureUsable();
        lock (_gate)
        {
            using SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
                _protectedHandle.AcquireReference();
            CfCorrelationVector native = default;
            int result = CfApi.CfGetCorrelationVector(handle.Win32Handle, &native);
            ThrowIfFailed("CloudItemLease.GetCorrelationVector", Item.FullPath, result);
            return CloudCorrelationVector.FromNative(native);
        }
    }

    /// <summary>Sets the native correlation vector for this item.</summary>
    /// <param name="vector">Validated vector copied into temporary native storage.</param>
    /// <exception cref="InvalidOperationException">The lease was not opened for write access.</exception>
    public void SetCorrelationVector(CloudCorrelationVector vector)
    {
        EnsureUsable();
        if (!_options.Access.HasFlag(CloudItemLeaseAccess.Write))
        {
            throw new InvalidOperationException("The lease must include write access to set a correlation vector.");
        }

        lock (_gate)
        {
            using SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
                _protectedHandle.AcquireReference();
            CfCorrelationVector native = vector.ToNative();
            int result = CfApi.CfSetCorrelationVector(handle.Win32Handle, &native);
            ThrowIfFailed("CloudItemLease.SetCorrelationVector", Item.FullPath, result);
        }
    }

    /// <summary>Acquires a transfer key for proactive Cloud Files operations.</summary>
    public ValueTask<CloudTransfer> BeginTransferAsync(
        CloudTransferOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureUsable();
        cancellationToken.ThrowIfCancellationRequested();
        CloudTransferOptions selected = options ?? CloudTransferOptions.Default;
        selected.Validate();
        lock (_gate)
        {
            EnsureUsable();
            if (_transfer is not null)
            {
                throw new InvalidOperationException("This item lease already owns an active transfer.");
            }

            SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
                _protectedHandle.AcquireReference();
            try
            {
                CfTransferKey transferKey = default;
                int result = CfApi.CfGetTransferKey(handle.Win32Handle, &transferKey);
                if (result < 0)
                {
                    throw CloudFilesException.FromHResult(
                        "CloudItemLease.BeginTransfer",
                        Item.FullPath,
                        result);
                }

                CloudTransfer transfer = new(
                    this,
                    selected,
                    handle,
                    transferKey,
                    _providerSession.ConnectionKey);
                _transfer = transfer;
                return new ValueTask<CloudTransfer>(transfer);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
    }

    /// <summary>Releases an active transfer, then the protected handle and operation admission.</summary>
    public void Dispose()
    {
        CloudTransfer? transfer;
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            transfer = _transfer;
            _transfer = null;
        }

        try
        {
            transfer?.Dispose();
        }
        finally
        {
            _protectedHandle.Dispose();
            _operation.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    internal void TransferDisposed(CloudTransfer transfer)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_transfer, transfer))
            {
                _transfer = null;
            }
        }
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (IsInvalidated)
        {
            throw new InvalidOperationException("The protected item lease has been invalidated.");
        }
    }

    private static void ThrowIfFailed(string operation, string path, int hresult)
    {
        if (hresult < 0)
        {
            throw CloudFilesException.FromHResult(operation, path, hresult);
        }
    }
}
