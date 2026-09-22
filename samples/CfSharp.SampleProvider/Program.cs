using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CfSharp;

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

        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: CfSharp.SampleProvider <content-directory> <sync-root-directory>");
            return 2;
        }

        string contentRoot = Path.GetFullPath(args[0]);
        string syncRootPath = Path.GetFullPath(args[1]);
        if (!Directory.Exists(contentRoot))
        {
            Console.Error.WriteLine($"Content directory does not exist: {contentRoot}");
            return 2;
        }

        Directory.CreateDirectory(syncRootPath);
        SyncRootRegistrationOptions registration =
            SyncRootRegistrationOptions.CreateBuilder("CfSharp Sample Provider", "0.1.0")
                .WithProviderId(ProviderId)
                .WithSyncRootIdentity(ProviderId.ToByteArray())
                .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
                .WithPopulationPolicy(CloudPopulationPolicy.Partial)
                .WithRootMarkedInSync()
                .Build();
        CloudSyncRoot syncRoot = CloudSyncRoot.Register(syncRootPath, registration);

        LocalFolderContentProvider provider = new(contentRoot, syncRootPath);
        await using CloudProviderSession session = CloudProviderSession.Connect(syncRoot, provider);

        // Use a separate process for the first enumeration. Windows does not always issue a
        // FETCH_PLACEHOLDERS callback for an enumeration initiated by the provider process
        // itself; an external consumer reliably exercises the same shell-facing path.
        using (Process enumerationProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c dir /s /b \"{syncRootPath}\" > nul",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!)
        {
            await enumerationProcess.WaitForExitAsync();
            if (enumerationProcess.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The external sync-root enumeration failed with exit code {enumerationProcess.ExitCode}.");
            }
        }

        // Descendant enumeration repeats this for every partial directory and exercises the
        // continuation-token path without requiring the sample to pre-materialize the tree.
        string[] placeholderFiles = Directory
            .EnumerateFiles(syncRootPath, "*", SearchOption.AllDirectories)
            .ToArray();
        List<Task<byte[]>> randomReads = [];
        foreach (string placeholderFile in placeholderFiles.Take(4))
        {
            string relativePath = Path.GetRelativePath(syncRootPath, placeholderFile);
            FileInfo sourceInfo = new(Path.Combine(contentRoot, relativePath));
            long offset = sourceInfo.Length == 0 ? 0 : sourceInfo.Length / 2;
            int length = (int)Math.Min(1024, sourceInfo.Length - offset);
            randomReads.Add(ReadRangeAsync(placeholderFile, offset, length));
        }

        byte[][] ranges = await Task.WhenAll(randomReads);

        Console.WriteLine($"Sync root: {syncRoot.Path}");
        Console.WriteLine($"Populated placeholders: {placeholderFiles.Length}");
        Console.WriteLine($"Concurrent random ranges hydrated: {ranges.Length}");
        Console.WriteLine("The registration remains installed; call CloudSyncRoot.Unregister only when removing it.");
        return 0;
    }

    private static async Task<byte[]> ReadRangeAsync(string path, long offset, int length)
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
        int read = await stream.ReadAsync(buffer);
        return buffer[..read];
    }

    private sealed class LocalFolderContentProvider : ICloudDemandProvider
    {
        private readonly string _rootPath;
        private readonly string _rootPrefix;
        private readonly string _syncRootPath;

        internal LocalFolderContentProvider(string rootPath, string syncRootPath)
        {
            _rootPath = Path.GetFullPath(rootPath);
            _rootPrefix = _rootPath + Path.DirectorySeparatorChar;
            _syncRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(syncRootPath));
        }

        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string remotePath = CloudPlaceholderIdentity
                .Decode(request.FileIdentity)
                .RemoteId;
            string relativePath = Path.GetRelativePath(_rootPath, remotePath);
            string contentPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
            if (!contentPath.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The placeholder identity escapes the content directory.");
            }

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
            string relativePath = normalizedPath;
            if (Path.IsPathRooted(relativePath) &&
                relativePath.StartsWith(_syncRootPath, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = Path.GetRelativePath(_syncRootPath, relativePath);
            }

            relativePath = relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string sourcePath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
            if (!sourcePath.Equals(_rootPath, StringComparison.OrdinalIgnoreCase) &&
                !sourcePath.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The directory callback escapes the content directory.");
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
