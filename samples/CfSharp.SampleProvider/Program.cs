using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using CfSharp;
using CfSharp.Storage.Sqlite;

return await SampleProvider.RunAsync(args);

internal static class SampleProvider
{
    private static readonly Guid ProviderId = new("BD3DAA90-BDEA-48E4-8257-A0C7D35A6803");

    internal static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            Console.Error.WriteLine("CfSharp requires Windows 10 version 1709 or later.");
            return 1;
        }

        if (!SampleArguments.TryParse(args, out SampleArguments? options, out string error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        SampleArguments selectedOptions = options!;

        if (selectedOptions.Command is SampleCommand.Register or SampleCommand.Unregister &&
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            Console.Error.WriteLine(
                "CfSharp Sample Shell registration requires Windows 10 version 2004 or later.");
            return 1;
        }

        try
        {
            return selectedOptions.Command switch
            {
                SampleCommand.Register => await RegisterAsync(selectedOptions.SyncRootPath),
                SampleCommand.Unregister => Unregister(selectedOptions.SyncRootPath),
                SampleCommand.Run => await RunProviderAsync(selectedOptions),
                _ => throw new InvalidOperationException("The sample command is not supported."),
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            CloudFilesException or InvalidOperationException)
        {
            Console.Error.WriteLine($"CfSharp Sample failed: {exception.Message}");
            return 1;
        }
    }

    [SupportedOSPlatform("windows10.0.19041")]
    private static async Task<int> RegisterAsync(string requestedSyncRootPath)
    {
        string syncRootPath = Path.GetFullPath(requestedSyncRootPath);
        Directory.CreateDirectory(syncRootPath);
        syncRootPath = SamplePathSafety.NormalizeExistingDirectory(syncRootPath, "sync-root");
        bool shellRegistrationAlreadyExisted =
            await ShellSyncRootRegistrar.RegisterAsync(syncRootPath);
        CloudSyncRoot syncRoot;
        try
        {
            syncRoot = CloudSyncRoot.Register(syncRootPath, CreateRegistration());
        }
        catch
        {
            if (!shellRegistrationAlreadyExisted)
            {
                try
                {
                    ShellSyncRootRegistrar.Unregister(syncRootPath);
                }
                catch
                {
                    // Preserve the original CFAPI failure; the unregister command can remove
                    // the Shell registration if Windows rejected the compensating cleanup.
                }
            }

            throw;
        }

        CloudSyncRootInfo info = syncRoot.GetInfo();
        Console.WriteLine($"Registered CfSharp Sample at {info.Path}");
        Console.WriteLine($"Provider: {info.ProviderName} {info.ProviderVersion}");
        return 0;
    }

    [SupportedOSPlatform("windows10.0.19041")]
    private static int Unregister(string requestedSyncRootPath)
    {
        string syncRootPath = SamplePathSafety.NormalizeExistingDirectory(
            requestedSyncRootPath,
            "sync-root");
        if (ShellSyncRootRegistrar.TryGetRegisteredPath(out string? existingShellPath) &&
            !string.Equals(
                Path.GetFullPath(existingShellPath!),
                syncRootPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The CfSharp Sample Shell registration belongs to '{existingShellPath}', not '{syncRootPath}'.");
        }

        CloudSyncRoot.Open(syncRootPath).Unregister();
        ShellSyncRootRegistrar.Unregister(syncRootPath);
        Console.WriteLine($"Unregistered CfSharp Sample from {syncRootPath}");
        return 0;
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static async Task<int> RunProviderAsync(SampleArguments options)
    {
        string contentRoot = SamplePathSafety.NormalizeExistingDirectory(
            options.ContentRoot!,
            "content");
        string syncRootPath = SamplePathSafety.NormalizeExistingDirectory(
            options.SyncRootPath,
            "sync-root");
        CloudSyncRoot syncRoot = CloudSyncRoot.Open(syncRootPath);
        LocalFolderContentProvider provider = new(contentRoot, syncRootPath);
        SqliteCloudStateStoreFactory stateStoreFactory = new(options.StateDatabasePath!);
        await using CloudFileSystem fileSystem = CloudFileSystem
            .CreateBuilder(syncRootPath)
            .WithStateStore(stateStoreFactory)
            .WithContentProvider(provider)
            .Build();

        using CancellationTokenSource shutdown = new();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            await fileSystem.StartAsync(shutdown.Token);
            await ExerciseShellFacingProviderAsync(syncRootPath, contentRoot, shutdown.Token);
            Console.WriteLine($"Sync root: {syncRoot.Path}");
            Console.WriteLine("The registration remains installed; use the unregister command for removal.");
            if (!options.RunOnce)
            {
                Console.WriteLine("Provider is running. Press Ctrl+C to stop without unregistering.");
                await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
            }

            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static async Task ExerciseShellFacingProviderAsync(
        string syncRootPath,
        string contentRoot,
        CancellationToken cancellationToken)
    {
        // Use a separate process for the first enumeration. Windows does not always issue a
        // FETCH_PLACEHOLDERS callback for an enumeration initiated by the provider process
        // itself; an external consumer reliably exercises the same shell-facing path.
        using (Process enumerationProcess = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!)
        {
            enumerationProcess.StartInfo.ArgumentList.Add("/d");
            enumerationProcess.StartInfo.ArgumentList.Add("/c");
            enumerationProcess.StartInfo.ArgumentList.Add("dir");
            enumerationProcess.StartInfo.ArgumentList.Add("/s");
            enumerationProcess.StartInfo.ArgumentList.Add("/b");
            enumerationProcess.StartInfo.ArgumentList.Add("/a:-l");
            enumerationProcess.StartInfo.ArgumentList.Add(syncRootPath);
            Task<string> outputTask = enumerationProcess.StandardOutput.ReadToEndAsync(
                CancellationToken.None);
            await enumerationProcess.WaitForExitAsync(cancellationToken);
            _ = await outputTask;
            if (enumerationProcess.ExitCode != 0)
            {
                string error = await enumerationProcess.StandardError.ReadToEndAsync(
                    CancellationToken.None);
                throw new InvalidOperationException(
                    $"The external sync-root enumeration failed with exit code {enumerationProcess.ExitCode}: {error}");
            }
        }

        // Descendant enumeration repeats this for every partial directory and exercises the
        // continuation-token path without requiring the sample to pre-materialize the tree.
        string[] placeholderFiles = Directory
            .EnumerateFiles(
                syncRootPath,
                "*",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                })
            .ToArray();
        List<Task<byte[]>> randomReads = [];
        foreach (string placeholderFile in placeholderFiles.Take(4))
        {
            string relativePath = Path.GetRelativePath(syncRootPath, placeholderFile);
            FileInfo sourceInfo = new(SamplePathSafety.ResolveContainedPath(
                contentRoot,
                Path.Combine(contentRoot, relativePath)));
            long offset = sourceInfo.Length == 0 ? 0 : sourceInfo.Length / 2;
            int length = (int)Math.Min(1024, sourceInfo.Length - offset);
            randomReads.Add(ReadRangeAsync(placeholderFile, offset, length, cancellationToken));
        }

        byte[][] ranges = await Task.WhenAll(randomReads);
        Console.WriteLine($"Populated placeholders: {placeholderFiles.Length}");
        Console.WriteLine($"Concurrent random ranges hydrated: {ranges.Length}");
    }

    private static async Task<byte[]> ReadRangeAsync(
        string path,
        long offset,
        int length,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        stream.Position = offset;
        byte[] buffer = new byte[length];
        int read = await stream.ReadAsync(buffer, cancellationToken);
        return buffer[..read];
    }

    private static SyncRootRegistrationOptions CreateRegistration() =>
        SyncRootRegistrationOptions.CreateBuilder("CfSharp Sample", "0.1.0")
            .WithProviderId(ProviderId)
            .WithSyncRootIdentity(ProviderId.ToByteArray())
            .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
            .WithPopulationPolicy(CloudPopulationPolicy.Partial)
            .WithRootMarkedInSync()
            .WithExistingRegistrationUpdate()
            .Build();

    private sealed class LocalFolderContentProvider : ICloudDemandProvider
    {
        private readonly string _rootPath;
        private readonly string _syncRootPath;

        internal LocalFolderContentProvider(string rootPath, string syncRootPath)
        {
            _rootPath = SamplePathSafety.NormalizeExistingDirectory(rootPath, "content");
            _syncRootPath = SamplePathSafety.NormalizeExistingDirectory(syncRootPath, "sync-root");
        }

        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string remotePath = CloudPlaceholderIdentity
                .Decode(request.FileIdentity)
                .RemoteId;
            string contentPath = SamplePathSafety.ResolveContainedPath(_rootPath, remotePath);

            Stream stream = new FileStream(
                contentPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return ValueTask.FromResult(stream);
        }

        public ValueTask<CloudProviderDirectoryPage> FetchChildrenAsync(
            CloudProviderFetchPlaceholdersRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourceDirectory = ResolveSourcePath(request.NormalizedPath);
            string pattern = string.IsNullOrWhiteSpace(request.SearchPattern)
                ? "*"
                : request.SearchPattern;
            string[] entries = Directory
                .EnumerateFileSystemEntries(sourceDirectory, pattern, SearchOption.TopDirectoryOnly)
                .Where(static path =>
                {
                    SamplePathSafety.RejectReparsePoints(path);
                    return true;
                })
                .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();
            int offset = ParseContinuation(request.ContinuationToken, entries.Length);
            const int pageSize = 128;
            CloudPlaceholderSpec[] children = entries
                .Skip(offset)
                .Take(pageSize)
                .Select(CreatePlaceholder)
                .ToArray();
            int nextOffset = offset + children.Length;
            string? continuation = nextOffset < entries.Length
                ? nextOffset.ToString(CultureInfo.InvariantCulture)
                : null;
            return ValueTask.FromResult(new CloudProviderDirectoryPage(
                children,
                continuation,
                entries.Length));
        }

        private string ResolveSourcePath(string normalizedPath)
        {
            string callbackPath = SamplePathSafety.ResolveSyncRootCallbackPath(
                _syncRootPath,
                normalizedPath);
            string relativePath = Path.GetRelativePath(_syncRootPath, callbackPath);
            if (relativePath == ".")
            {
                relativePath = string.Empty;
            }
            string sourcePath = SamplePathSafety.ResolveContainedPath(
                _rootPath,
                Path.Combine(_rootPath, relativePath));
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException(
                    $"The content directory does not exist: {sourcePath}");
            }

            return sourcePath;
        }

        private static int ParseContinuation(string? token, int count)
        {
            if (token is null)
            {
                return 0;
            }

            if (!int.TryParse(token, out int offset) || offset < 0 || offset > count)
            {
                throw new InvalidDataException("The directory continuation token is invalid.");
            }

            return offset;
        }

        private static CloudPlaceholderSpec CreatePlaceholder(string path)
        {
            SamplePathSafety.RejectReparsePoints(path);
            string name = Path.GetFileName(path);
            string remoteId = path;
            CloudPlaceholderIdentity identity = new(CreateStableItemId(remoteId), remoteId);
            if (Directory.Exists(path))
            {
                return CloudDirectoryPlaceholderSpec.CreateBuilder(name, identity)
                    .WithPopulationState(CloudDirectoryPopulationState.Partial)
                    .Build();
            }

            long length = new FileInfo(path).Length;
            return CloudFilePlaceholderSpec.CreateBuilder(name, identity, length)
                .WithInitialAvailability(CloudAvailabilityTarget.OnlineOnly)
                .Build();
        }

        private static Guid CreateStableItemId(string remoteId)
        {
            Span<byte> digest = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes(remoteId), digest);
            return new Guid(digest[..16], bigEndian: true);
        }
    }
}
