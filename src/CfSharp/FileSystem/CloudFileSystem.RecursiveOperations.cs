namespace CfSharp;

public sealed partial class CloudFileSystem
{
    internal async ValueTask<CloudRecursiveOperationResult> SetPinStateRecursivelyAsync(
        CloudDirectory directory,
        CloudPinTarget target,
        CloudRecursiveOperationOptions options,
        CancellationToken cancellationToken)
    {
        CloudPlaceholderConversionOptions.RequireDefined(target, nameof(target));
        ArgumentNullException.ThrowIfNull(options);
        using CloudFileSystemOperationLease operation = await AcquireOperationAsync(
            [CloudItemOperationScope.Subtree(directory.FullPath)],
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RecursiveItemEntry> entries = MaterializeLocalTree(
            directory,
            childrenFirst: false,
            options.IncludeRoot);
        return await ApplyRecursiveNativeOperation(
            entries.Where(static entry => !entry.IsLink).ToArray(),
            options.StopOnFirstFailure,
            (entry, _) =>
            {
                CloudFileStatePlatform.SetPinState(entry.FullPath, target);
                return ValueTask.CompletedTask;
            },
            cancellationToken);
    }

    internal async ValueTask<CloudRecursiveOperationResult> SetAvailabilityRecursivelyAsync(
        CloudDirectory directory,
        CloudAvailabilityTarget target,
        CloudRecursiveOperationOptions options,
        CancellationToken cancellationToken)
    {
        CloudPlaceholderConversionOptions.RequireDefined(target, nameof(target));
        ArgumentNullException.ThrowIfNull(options);
        using CloudFileSystemOperationLease operation = await AcquireOperationAsync(
            [CloudItemOperationScope.Subtree(directory.FullPath)],
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RecursiveItemEntry> entries = MaterializeLocalTree(
            directory,
            childrenFirst: false,
            options.IncludeRoot);
        return await ApplyRecursiveNativeOperation(
            entries.Where(static entry => !entry.IsLink).ToArray(),
            options.StopOnFirstFailure,
            (entry, token) => ApplyAvailabilityAsync(entry, target, token),
            cancellationToken);
    }

    internal async ValueTask<CloudRecursiveOperationResult> DeleteTreeAsync(
        CloudDirectory directory,
        CloudRecursiveOperationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (directory.RelativePath.Length == 0 && options.IncludeRoot)
        {
            throw new InvalidOperationException(
                "The sync root cannot be deleted; exclude the root to delete its local children.");
        }

        using CloudFileSystemOperationLease operation = await AcquireOperationAsync(
            [CloudItemOperationScope.Subtree(directory.FullPath)],
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RecursiveItemEntry> entries = MaterializeLocalTree(
            directory,
            childrenFirst: true,
            options.IncludeRoot);
        List<CloudRecursiveOperationEntryResult> results = new(entries.Count);
        bool stopped = false;
        try
        {
            foreach (RecursiveItemEntry entry in entries)
            {
                if (stopped)
                {
                    results.Add(CreateRecursiveResult(
                        entry,
                        CloudItemOperationStatus.NotProcessed,
                        error: null));
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    DeleteLocalItem(entry.FullPath, entry.Kind, "CloudDirectory.DeleteTree");
                }
                catch (CloudFilesException exception)
                {
                    results.Add(CreateRecursiveResult(
                        entry,
                        CloudItemOperationStatus.Failed,
                        exception));
                    stopped = options.StopOnFirstFailure;
                    continue;
                }

                try
                {
                    _ = await PersistTombstoneAsync(
                        operation.StateStore,
                        entry.RelativePath,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    results.Add(CreateRecursiveResult(
                        entry,
                        CloudItemOperationStatus.Succeeded,
                        error: null));
                    throw new CloudItemCoordinationException(
                        "CloudDirectory.DeleteTree",
                        entry.FullPath,
                        operationUsn: null,
                        exception,
                        new CloudRecursiveOperationResult(results));
                }

                results.Add(CreateRecursiveResult(
                    entry,
                    CloudItemOperationStatus.Succeeded,
                    error: null));
            }
        }
        catch (OperationCanceledException exception)
        {
            throw new CloudRecursiveOperationCanceledException(
                new CloudRecursiveOperationResult(results),
                exception,
                cancellationToken);
        }

        return new CloudRecursiveOperationResult(results);
    }

    private static async ValueTask<CloudRecursiveOperationResult> ApplyRecursiveNativeOperation(
        IReadOnlyList<RecursiveItemEntry> entries,
        bool stopOnFirstFailure,
        Func<RecursiveItemEntry, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken)
    {
        List<CloudRecursiveOperationEntryResult> results = new(entries.Count);
        bool stopped = false;
        foreach (RecursiveItemEntry entry in entries)
        {
            if (stopped)
            {
                results.Add(CreateRecursiveResult(
                    entry,
                    CloudItemOperationStatus.NotProcessed,
                    error: null));
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await operation(entry, cancellationToken).ConfigureAwait(false);
                results.Add(CreateRecursiveResult(
                    entry,
                    CloudItemOperationStatus.Succeeded,
                    error: null));
            }
            catch (CloudFilesException exception)
            {
                results.Add(CreateRecursiveResult(
                    entry,
                    CloudItemOperationStatus.Failed,
                    exception));
                stopped = stopOnFirstFailure;
            }
        }

        return new CloudRecursiveOperationResult(results);
    }

    private static async ValueTask ApplyAvailabilityAsync(
        RecursiveItemEntry entry,
        CloudAvailabilityTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.Kind is CloudItemKind.Directory)
        {
            CloudFileStatePlatform.SetPinState(
                entry.FullPath,
                target is CloudAvailabilityTarget.AlwaysAvailable
                    ? CloudPinTarget.Pinned
                    : CloudPinTarget.Unpinned);
            return;
        }

        if (target is CloudAvailabilityTarget.OnlineOnly)
        {
            CloudFileStatePlatform.SetPinState(entry.FullPath, CloudPinTarget.Unpinned);

            cancellationToken.ThrowIfCancellationRequested();
            CloudFileStatePlatform.Dehydrate(
                entry.FullPath,
                CloudFileRange.WholeFile,
                CloudDehydrationOptions.Foreground);
        }
        else
        {
            // Hydration must finish before finalizing pin intent. A preceding pin transition may
            // otherwise race provider work that Windows starts for the same placeholder.
            await HydrateWithTransientRetryAsync(
                entry.FullPath,
                CloudFileRange.WholeFile,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            CloudFileStatePlatform.SetPinState(
                entry.FullPath,
                target is CloudAvailabilityTarget.AlwaysAvailable
                    ? CloudPinTarget.Pinned
                    : CloudPinTarget.Unpinned);
        }
    }

    private static List<RecursiveItemEntry> MaterializeLocalTree(
        CloudDirectory directory,
        bool childrenFirst,
        bool includeRoot)
    {
        if (!Directory.Exists(directory.FullPath))
        {
            throw new DirectoryNotFoundException(
                $"The cloud directory does not exist: '{directory.FullPath}'.");
        }

        List<RecursiveItemEntry> entries = [];
        VisitLocalTree(
            directory.FullPath,
            directory.RelativePath,
            childrenFirst,
            includeCurrent: includeRoot,
            entries);
        return entries;
    }

    private static void VisitLocalTree(
        string fullPath,
        string relativePath,
        bool childrenFirst,
        bool includeCurrent,
        List<RecursiveItemEntry> entries)
    {
        Stack<TreeVisit> pending = new();
        pending.Push(new(fullPath, relativePath, includeCurrent, PostVisit: false));
        while (pending.Count != 0)
        {
            TreeVisit visit = pending.Pop();
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(visit.FullPath);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            CloudItemKind kind = attributes.HasFlag(FileAttributes.Directory)
                ? CloudItemKind.Directory
                : CloudItemKind.File;
            bool isLink = IsFileSystemLink(visit.FullPath, kind, attributes);
            RecursiveItemEntry current = new(visit.FullPath, visit.RelativePath, kind, isLink);
            if (visit.PostVisit || !childrenFirst || kind is CloudItemKind.File || isLink)
            {
                if (visit.IncludeCurrent)
                {
                    entries.Add(current);
                }
            }

            if (visit.PostVisit || kind is not CloudItemKind.Directory || isLink)
            {
                continue;
            }

            string[] children;
            try
            {
                children = Directory.GetFileSystemEntries(visit.FullPath);
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            Array.Sort(children, CompareEntryPaths);
            if (childrenFirst && visit.IncludeCurrent)
            {
                pending.Push(new(visit.FullPath, visit.RelativePath, true, PostVisit: true));
            }

            for (int index = children.Length - 1; index >= 0; index--)
            {
                string child = children[index];
                string childRelativePath = visit.RelativePath.Length == 0
                    ? Path.GetFileName(child)
                    : Path.Combine(visit.RelativePath, Path.GetFileName(child));
                pending.Push(new(child, childRelativePath, true, PostVisit: false));
            }
        }
    }

    private readonly record struct TreeVisit(
        string FullPath,
        string RelativePath,
        bool IncludeCurrent,
        bool PostVisit);

    private static bool IsFileSystemLink(
        string path,
        CloudItemKind kind,
        FileAttributes attributes)
    {
        if (!attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        FileSystemInfo info = kind is CloudItemKind.Directory
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        if (info.LinkTarget is not null)
        {
            return true;
        }

        // LinkTarget is null for reparse points whose tag is unknown to the BCL. Fail closed for
        // those points unless Cloud Files can positively identify its own placeholder tag; never
        // recurse through an unclassified directory that might redirect outside the sync root.
        return !CloudItemInspector.IsCloudPlaceholder(path);
    }

    private static int CompareEntryPaths(string left, string right)
    {
        string leftName = Path.GetFileName(left);
        string rightName = Path.GetFileName(right);
        int comparison = StringComparer.OrdinalIgnoreCase.Compare(leftName, rightName);
        return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(leftName, rightName);
    }

    private static CloudRecursiveOperationEntryResult CreateRecursiveResult(
        RecursiveItemEntry entry,
        CloudItemOperationStatus status,
        CloudFilesException? error) =>
        new(entry.FullPath, entry.Kind, status, error);

    private sealed record RecursiveItemEntry(
        string FullPath,
        string RelativePath,
        CloudItemKind Kind,
        bool IsLink);
}
