namespace CfSharp;

public sealed partial class CloudFileSystem
{
    internal async ValueTask<CloudStateChangeResult> SetPinStateAsync(
        CloudItem item,
        CloudPinTarget target,
        CancellationToken cancellationToken)
    {
        CloudPlaceholderConversionOptions.RequireDefined(target, nameof(target));
        using CloudFileSystemOperationLease operation = await AcquireOperationAsync(
            [CloudItemOperationScope.Exact(item.FullPath)],
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        CloudFileStatePlatform.SetPinState(item.FullPath, target);

        CloudItemSnapshot snapshot = await InspectCoreAsync(
            item,
            operation.StateStore,
            CancellationToken.None).ConfigureAwait(false);
        return new CloudStateChangeResult(item.FullPath, operationUsn: null, snapshot);
    }

    internal async ValueTask<CloudStateChangeResult> SetInSyncAsync(
        CloudItem item,
        bool inSync,
        CloudInSyncChangeOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        using CloudFileSystemOperationLease operation = await AcquireOperationAsync(
            [CloudItemOperationScope.Exact(item.FullPath)],
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        long operationUsn = CloudFileStatePlatform.SetInSyncState(
            item.FullPath,
            inSync,
            options.ExpectedUsn);

        CloudItemSnapshot snapshot = await InspectCoreAsync(
            item,
            operation.StateStore,
            CancellationToken.None).ConfigureAwait(false);
        return new CloudStateChangeResult(item.FullPath, operationUsn, snapshot);
    }

    internal async ValueTask HydrateAsync(
        CloudFile file,
        CloudFileRange range,
        CancellationToken cancellationToken)
    {
        range.Validate(nameof(range));
        using CloudFileSystemOperationLease operation = await AcquireOperationAsync(
            [CloudItemOperationScope.Exact(file.FullPath)],
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        CloudFileStatePlatform.Hydrate(file.FullPath, range);
    }

    internal async ValueTask DehydrateAsync(
        CloudFile file,
        CloudFileRange range,
        CloudDehydrationOptions options,
        CancellationToken cancellationToken)
    {
        range.Validate(nameof(range));
        ArgumentNullException.ThrowIfNull(options);
        using CloudFileSystemOperationLease operation = await AcquireOperationAsync(
            [CloudItemOperationScope.Exact(file.FullPath)],
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        CloudFileStatePlatform.Dehydrate(file.FullPath, range, options);
    }

    internal async ValueTask<IReadOnlyList<CloudFileRange>> GetRangesAsync(
        CloudFile file,
        CloudPlaceholderRangeKind kind,
        CloudFileRange range,
        CancellationToken cancellationToken)
    {
        CloudPlaceholderConversionOptions.RequireDefined(kind, nameof(kind));
        range.Validate(nameof(range));
        using CloudFileSystemOperationLease operation = await AcquireOperationAsync(
            [CloudItemOperationScope.Exact(file.FullPath)],
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return CloudFileStatePlatform.GetRanges(
            file.FullPath,
            kind,
            range,
            cancellationToken);
    }

    internal async ValueTask<CloudAvailabilityChangeResult> SetAvailabilityAsync(
        CloudFile file,
        CloudAvailabilityTarget target,
        CancellationToken cancellationToken)
    {
        CloudPlaceholderConversionOptions.RequireDefined(target, nameof(target));
        using CloudFileSystemOperationLease operation = await AcquireOperationAsync(
            [CloudItemOperationScope.Exact(file.FullPath)],
            cancellationToken).ConfigureAwait(false);

        bool pinStateApplied = false;
        bool contentStateApplied = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target is CloudAvailabilityTarget.OnlineOnly)
            {
                CloudFileStatePlatform.SetPinState(file.FullPath, CloudPinTarget.Unpinned);
                pinStateApplied = true;

                cancellationToken.ThrowIfCancellationRequested();
                CloudFileStatePlatform.Dehydrate(
                    file.FullPath,
                    CloudFileRange.WholeFile,
                    CloudDehydrationOptions.Foreground);
                contentStateApplied = true;
            }
            else
            {
                // Applying the final pin intent after synchronous hydration avoids racing work
                // that Windows or another sync-engine component may start for a newly pinned file.
                CloudFileStatePlatform.Hydrate(file.FullPath, CloudFileRange.WholeFile);
                contentStateApplied = true;

                cancellationToken.ThrowIfCancellationRequested();
                CloudFileStatePlatform.SetPinState(
                    file.FullPath,
                    target is CloudAvailabilityTarget.AlwaysAvailable
                        ? CloudPinTarget.Pinned
                        : CloudPinTarget.Unpinned);
                pinStateApplied = true;
            }
        }
        catch (CloudFilesException failure)
        {
            CloudItemSnapshot failedSnapshot = await InspectCoreAsync(
                file,
                operation.StateStore,
                CancellationToken.None).ConfigureAwait(false);
            throw new CloudAvailabilityTransitionException(
                failure,
                new CloudAvailabilityChangeResult(
                    file.FullPath,
                    target,
                    pinStateApplied,
                    contentStateApplied,
                    failedSnapshot));
        }

        CloudItemSnapshot snapshot = await InspectCoreAsync(
            file,
            operation.StateStore,
            CancellationToken.None).ConfigureAwait(false);
        return new CloudAvailabilityChangeResult(
            file.FullPath,
            target,
            pinStateApplied,
            contentStateApplied,
            snapshot);
    }
}
