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
                .WithPopulationPolicy(CloudPopulationPolicy.AlwaysFull)
                .WithRootMarkedInSync()
                .Build();
        CloudSyncRoot syncRoot = CloudSyncRoot.Register(syncRootPath, registration);

        await using CloudProviderSession session = CloudProviderSession.Connect(
            syncRoot,
            new LocalFolderContentProvider(contentRoot));

        int created = 0;
        int verified = 0;
        foreach (string contentFile in Directory.EnumerateFiles(
            contentRoot,
            "*",
            SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(contentRoot, contentFile);
            string placeholderPath = Path.Combine(syncRootPath, relativePath);
            string? parentPath = Path.GetDirectoryName(placeholderPath);
            if (parentPath is not null)
            {
                Directory.CreateDirectory(parentPath);
            }

            if (!File.Exists(placeholderPath))
            {
                FileInfo contentInfo = new(contentFile);
                syncRoot.CreateFilePlaceholder(
                    relativePath,
                    contentInfo.Length,
                    Encoding.UTF8.GetBytes(relativePath));
                created++;
            }

            byte[] expected = await File.ReadAllBytesAsync(contentFile);
            byte[] actual = await File.ReadAllBytesAsync(placeholderPath);
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                throw new InvalidDataException(
                    $"Hydrated content does not match the source file: {relativePath}");
            }

            verified++;
        }

        Console.WriteLine($"Sync root: {syncRoot.Path}");
        Console.WriteLine($"Created placeholders: {created}");
        Console.WriteLine($"Verified hydrated files: {verified}");
        Console.WriteLine("The registration remains installed; call CloudSyncRoot.Unregister only when removing it.");
        return 0;
    }

    private sealed class LocalFolderContentProvider : ICloudFileContentProvider
    {
        private readonly string _rootPath;
        private readonly string _rootPrefix;

        internal LocalFolderContentProvider(string rootPath)
        {
            _rootPath = Path.GetFullPath(rootPath);
            _rootPrefix = _rootPath + Path.DirectorySeparatorChar;
        }

        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Encoding.UTF8.GetString(request.FileIdentity);
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
    }
}
