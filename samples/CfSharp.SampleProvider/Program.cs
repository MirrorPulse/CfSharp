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
    private const int CloudRootNotRegisteredHResult = unchecked((int)0x80070186);
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
                SampleCommand.Enumerate => Enumerate(selectedOptions.SyncRootPath),
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
        bool shellRegistrationAlreadyExists =
            ShellSyncRootRegistrar.TryGetRegisteredPath(out string? existingShellPath);
        if (shellRegistrationAlreadyExists &&
            !string.Equals(
                Path.GetFullPath(existingShellPath!),
                syncRootPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The CfSharp Sample Shell registration belongs to '{existingShellPath}', not '{syncRootPath}'.");
        }

        try
        {
            CloudSyncRoot.Open(syncRootPath).Unregister();
        }
        catch (CloudFilesException exception) when (
            shellRegistrationAlreadyExists &&
            IsStaleCloudRootRegistration(exception))
        {
            Console.Error.WriteLine(
                "The Cloud Files registration is already absent; removing the matching stale " +
                "Shell registration while preserving all other native failures.");
        }

        ShellSyncRootRegistrar.Unregister(syncRootPath);
        Console.WriteLine($"Unregistered CfSharp Sample from {syncRootPath}");
        return 0;
    }

    internal static bool IsStaleCloudRootRegistration(CloudFilesException exception) =>
        exception.Operation == "CloudSyncRoot.GetInfo" &&
        exception.HResult == CloudRootNotRegisteredHResult;

    private static int Enumerate(string requestedSyncRootPath)
    {
        string syncRootPath = SamplePathSafety.NormalizeExistingDirectory(
            requestedSyncRootPath,
            "sync-root");
        foreach (string path in Directory.EnumerateFiles(
                     syncRootPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            Console.WriteLine(path);
        }

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
        using ActivityListener? diagnostics = SampleDiagnostics.TryEnable();
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
        // Use a separate, controlled consumer process for the first enumeration. This is
        // important because Windows does not always issue FETCH_PLACEHOLDERS for an enumeration
        // initiated by the provider process itself. The consumer receives arguments directly and
        // never routes paths through cmd.exe.
        ProcessStartInfo enumerationStartInfo = CreateEnumerationProcessStartInfo(syncRootPath);
        using Process enumerationProcess = Process.Start(enumerationStartInfo) ??
            throw new InvalidOperationException("The external sync-root enumeration could not start.");
        Task<string> outputTask = enumerationProcess.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> errorTask = enumerationProcess.StandardError.ReadToEndAsync(CancellationToken.None);
        await enumerationProcess.WaitForExitAsync(cancellationToken);
        string output = await outputTask;
        string error = await errorTask;
        if (enumerationProcess.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The external sync-root enumeration failed with exit code " +
                $"{enumerationProcess.ExitCode}: {error}");
        }

        string[] placeholderFiles = output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(path => SamplePathSafety.ResolveSyncRootCallbackPath(syncRootPath, path))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (placeholderFiles.Length == 0)
        {
            throw new InvalidOperationException(
                "The sync-root enumeration returned no files; the sample self-check cannot " +
                "report a successful hydration run for an empty result.");
        }

        // Descendant enumeration repeats this for every partial directory and exercises the
        // continuation-token path without requiring the sample to pre-materialize the tree.
        List<Task> externalReads = [];
        foreach (string placeholderFile in placeholderFiles.Take(4))
        {
            string relativePath = Path.GetRelativePath(syncRootPath, placeholderFile);
            string sourcePath = SamplePathSafety.ResolveContainedPath(
                contentRoot,
                Path.Combine(contentRoot, relativePath));
            FileInfo sourceInfo = new(sourcePath);
            if (sourceInfo.Length == 0)
            {
                continue;
            }

            externalReads.Add(VerifyExternalContentAsync(
                placeholderFile,
                sourcePath,
                cancellationToken));
        }

        if (externalReads.Count == 0)
        {
            throw new InvalidOperationException(
                "The sync-root enumeration returned no non-empty files for the concurrent " +
                "hydration self-check.");
        }

        await Task.WhenAll(externalReads);
        Console.WriteLine($"Populated placeholders: {placeholderFiles.Length}");
        Console.WriteLine($"Concurrent external hydrations verified: {externalReads.Count}");
    }

    internal static ProcessStartInfo CreateEnumerationProcessStartInfo(string syncRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRootPath);
        string processPath = Environment.ProcessPath ??
            throw new InvalidOperationException("The sample process path is unavailable.");
        string[] commandLine = Environment.GetCommandLineArgs();
        ProcessStartInfo startInfo = new()
        {
            FileName = processPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (Path.GetExtension(processPath).Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(commandLine[0]);
        }

        startInfo.ArgumentList.Add("enumerate");
        startInfo.ArgumentList.Add(syncRootPath);
        return startInfo;
    }

    internal static async Task VerifyExternalContentAsync(
        string placeholderPath,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        using Process comparison = Process.Start(CreateContentComparisonProcessStartInfo(
            sourcePath,
            placeholderPath)) ?? throw new InvalidOperationException(
                "The external content comparison process could not be started.");
        try
        {
            Task<string> outputTask = comparison.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> errorTask = comparison.StandardError.ReadToEndAsync(CancellationToken.None);
            await comparison.WaitForExitAsync(cancellationToken);
            string output = await outputTask;
            string error = await errorTask;
            if (comparison.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"The external content comparison failed for '{placeholderPath}' with exit " +
                    $"code {comparison.ExitCode}: {error}{output}");
            }
        }
        finally
        {
            if (!comparison.HasExited)
            {
                comparison.Kill(entireProcessTree: true);
                await comparison.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    internal static ProcessStartInfo CreateContentComparisonProcessStartInfo(
        string sourcePath,
        string placeholderPath)
    {
        ProcessStartInfo startInfo = new()
        {
            // Start fc.exe directly. ArgumentList is safe for the executable and does not pass
            // file names through cmd.exe's metacharacter grammar.
            FileName = Path.Combine(Environment.SystemDirectory, "fc.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/b");
        startInfo.ArgumentList.Add("/offline");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add(placeholderPath);
        return startInfo;
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
