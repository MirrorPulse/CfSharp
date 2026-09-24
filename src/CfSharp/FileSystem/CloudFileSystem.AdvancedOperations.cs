using System.Runtime.Versioning;

namespace CfSharp;

public sealed partial class CloudFileSystem
{
    [SupportedOSPlatform("windows10.0.16299")]
    internal async ValueTask<CloudItemLease> AcquireLeaseAsync(
        CloudItem item,
        CloudItemLeaseOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(options);
        if (!item.IsOwnedBy(this))
        {
            throw new ArgumentException("The item belongs to another cloud file system.", nameof(item));
        }

        options.Validate();
        CloudFileSystemOperationLease operation = await AcquireOperationAsync(
            [CloudItemOperationScope.Exact(item.FullPath)],
            cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runtimeSession is not ICloudTransferRuntimeSession transferRuntime)
            {
                throw new InvalidOperationException(
                    "An active provider session is required to acquire a CloudItemLease.");
            }

            CloudProviderSession providerSession = transferRuntime.ProviderSession;
            CfSharp.Native.CfOpenFileFlags flags = CfSharp.Native.CfOpenFileFlags.None;
            if (options.Access.HasFlag(CloudItemLeaseAccess.Write))
            {
                flags |= CfSharp.Native.CfOpenFileFlags.WriteAccess;
            }

            if (options.Exclusive)
            {
                flags |= CfSharp.Native.CfOpenFileFlags.Exclusive;
            }

            if (options.Foreground)
            {
                flags |= CfSharp.Native.CfOpenFileFlags.Foreground;
            }

            if (options.DeleteAccess)
            {
                flags |= CfSharp.Native.CfOpenFileFlags.DeleteAccess;
            }

            SafeCloudFilesProtectedHandle protectedHandle = SafeCloudFilesProtectedHandle.Open(
                item.FullPath,
                flags,
                "CloudItem.AcquireLease.Open");
            long? logicalLength = item.Kind == CloudItemKind.File
                ? CloudItemLease.ReadLogicalLength(protectedHandle)
                : null;
            return new CloudItemLease(
                item,
                options,
                operation,
                providerSession,
                protectedHandle,
                logicalLength);
        }
        catch
        {
            operation.Dispose();
            throw;
        }
    }
}
