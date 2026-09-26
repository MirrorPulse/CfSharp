using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private readonly long? _logicalLength;
    private readonly Activity? _activity;
    private CloudTransfer? _transfer;
    private int _disposed;

    internal CloudItemLease(
        CloudItem item,
        CloudItemLeaseOptions options,
        CloudFileSystem.CloudFileSystemOperationLease operation,
        CloudProviderSession providerSession,
        SafeCloudFilesProtectedHandle protectedHandle,
        long? logicalLength)
    {
        Item = item;
        _options = options;
        _operation = operation;
        _providerSession = providerSession;
        _protectedHandle = protectedHandle;
        _logicalLength = logicalLength;
        _activity = CloudDiagnostics.StartActivity("cfsharp.item.lease", "lease");
        CloudDiagnostics.RecordLeaseLifetime(created: true);
    }

    /// <summary>Releases leaked native and operation resources as a last-resort safety net.</summary>
    ~CloudItemLease()
    {
        // A missed Dispose must not keep the file-system admission lease alive forever. The
        // finalizer is deliberately a last-resort, non-throwing release path; normal callers
        // should still dispose explicitly so native errors remain observable.
        try
        {
            Dispose(disposing: false);
            CloudDiagnostics.RecordFinalizerRecovery("cfsharp.item.lease", recovered: true);
        }
        catch
        {
            CloudDiagnostics.RecordFinalizerRecovery("cfsharp.item.lease", recovered: false);
        }
    }

    /// <summary>Gets the immutable item reference held by this lease.</summary>
    public CloudItem Item { get; }

    /// <summary>Gets the validated access options used to open the lease.</summary>
    public CloudItemLeaseOptions Options => _options;

    /// <summary>
    /// Gets the logical file length captured when the protected lease was opened, when available.
    /// </summary>
    /// <remarks>
    /// The value is a lease-time snapshot. Windows remains authoritative if the protected handle
    /// is invalidated or the file changes after the lease is opened.
    /// </remarks>
    internal long? LogicalLength => _logicalLength;

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
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
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
            CloudDiagnostics.StopActivity(_activity, "disposed");
            CloudDiagnostics.RecordLeaseLifetime(created: false);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Dispose();
        GC.SuppressFinalize(this);
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

    internal static long? ReadLogicalLength(
        SafeCloudFilesProtectedHandle protectedHandle)
    {
        using SafeCloudFilesProtectedHandle.CloudFilesHandleReference handle =
            protectedHandle.AcquireReference();
        FileStandardInfo info = default;
        if (!GetFileInformationByHandleEx(
                handle.Win32Handle,
                FileStandardInformation,
                ref info,
                Marshal.SizeOf<FileStandardInfo>()))
        {
            return null;
        }

        return info.IsDirectory != 0 || info.EndOfFile < 0 ? null : info.EndOfFile;
    }

    private const int FileStandardInformation = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        nint fileHandle,
        int fileInformationClass,
        ref FileStandardInfo fileInformation,
        int bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        internal long AllocationSize;
        internal long EndOfFile;
        internal uint NumberOfLinks;
        internal byte DeletePending;
        internal byte IsDirectory;
    }
}
