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
        return ApplyRecursiveNativeOperation(
            entries.Where(static entry => !entry.IsLink).ToArray(),
            options.StopOnFirstFailure,
            entry => CloudFileStatePlatform.SetPinState(entry.FullPath, target),
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
        return ApplyRecursiveNativeOperation(
            entries.Where(static entry => !entry.IsLink).ToArray(),
            options.StopOnFirstFailure,
            entry => ApplyAvailability(entry, target, cancellationToken),
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
                throw new CloudItemCoordinationException(
                    "CloudDirectory.DeleteTree",
                    entry.FullPath,
                    operationUsn: null,
                    exception);
            }

            results.Add(CreateRecursiveResult(
                entry,
                CloudItemOperationStatus.Succeeded,
                error: null));
        }

        return new CloudRecursiveOperationResult(results);
    }

    private static CloudRecursiveOperationResult ApplyRecursiveNativeOperation(
        IReadOnlyList<RecursiveItemEntry> entries,
        bool stopOnFirstFailure,
        Action<RecursiveItemEntry> operation,
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
                operation(entry);
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

    private static void ApplyAvailability(
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
            CloudFileStatePlatform.Hydrate(entry.FullPath, CloudFileRange.WholeFile);

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
        FileAttributes attributes = File.GetAttributes(fullPath);
        CloudItemKind kind = attributes.HasFlag(FileAttributes.Directory)
            ? CloudItemKind.Directory
            : CloudItemKind.File;
        bool isLink = IsFileSystemLink(fullPath, kind, attributes);
        RecursiveItemEntry current = new(fullPath, relativePath, kind, isLink);
        if (includeCurrent && !childrenFirst)
        {
            entries.Add(current);
        }

        if (kind is CloudItemKind.Directory && !isLink)
        {
            string[] children = Directory.GetFileSystemEntries(fullPath);
            Array.Sort(children, CompareEntryPaths);
            foreach (string child in children)
            {
                FileAttributes childAttributes = File.GetAttributes(child);
                CloudItemKind childKind = childAttributes.HasFlag(FileAttributes.Directory)
                    ? CloudItemKind.Directory
                    : CloudItemKind.File;
                string childRelativePath = relativePath.Length == 0
                    ? Path.GetFileName(child)
                    : Path.Combine(relativePath, Path.GetFileName(child));
                if (childKind is CloudItemKind.Directory)
                {
                    VisitLocalTree(
                        child,
                        childRelativePath,
                        childrenFirst,
                        includeCurrent: true,
                        entries);
                }
                else
                {
                    entries.Add(new RecursiveItemEntry(
                        child,
                        childRelativePath,
                        childKind,
                        IsFileSystemLink(child, childKind, childAttributes)));
                }
            }
        }

        if (includeCurrent && childrenFirst)
        {
            entries.Add(current);
        }
    }

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
        return info.LinkTarget is not null;
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
