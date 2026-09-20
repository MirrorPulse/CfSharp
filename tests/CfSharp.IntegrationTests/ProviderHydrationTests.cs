using System.Runtime.Versioning;
using System.Text;

namespace CfSharp.IntegrationTests;

public sealed class ProviderHydrationTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task OrdinaryFileReadHydratesContentFromManagedProvider()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string testPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        string rootPath = Path.Combine(testPath, "sync");
        string contentPath = Path.Combine(testPath, "content");
        const string relativePath = "remote.bin";
        byte[] expected = new byte[150_123];
        new Random(8192).NextBytes(expected);
        CloudSyncRoot? root = null;
        CloudProviderSession? session = null;
        bool registered = false;

        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(contentPath);
        await File.WriteAllBytesAsync(Path.Combine(contentPath, relativePath), expected);

        try
        {
            Guid providerId = Guid.NewGuid();
            SyncRootRegistrationOptions options =
                SyncRootRegistrationOptions.CreateBuilder(
                        $"CfSharp Hydration {providerId:N}",
                        "1.0.0-test")
                    .WithProviderId(providerId)
                    .WithSyncRootIdentity(providerId.ToByteArray())
                    .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
                    .WithPopulationPolicy(CloudPopulationPolicy.AlwaysFull)
                    .WithRootMarkedInSync()
                    .Build();

            root = CloudSyncRoot.Register(rootPath, options);
            registered = true;
            session = CloudProviderSession.Connect(
                root,
                new LocalFolderContentProvider(contentPath));
            CloudPlaceholderCreationResult placeholder = root.CreateFilePlaceholder(
                relativePath,
                expected.Length,
                Encoding.UTF8.GetBytes(relativePath));

            byte[] actual = await File.ReadAllBytesAsync(placeholder.Path);

            Assert.Equal(expected, actual);
            Assert.NotEqual(0, placeholder.CreateUsn);

            await session.DisposeAsync();
            session = null;
            root.Unregister();
            registered = false;
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            if (registered && root is not null)
            {
                try
                {
                    root.Unregister();
                }
                catch (CloudFilesException)
                {
                    // Preserve the original failure while still attempting system cleanup.
                }
            }

            if (Directory.Exists(testPath))
            {
                Directory.Delete(testPath, recursive: true);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task ProviderExceptionCompletesReadWithFailure()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string rootPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        CloudSyncRoot? root = null;
        CloudProviderSession? session = null;
        bool registered = false;
        Directory.CreateDirectory(rootPath);

        try
        {
            root = RegisterRoot(rootPath, "Failure");
            registered = true;
            session = CloudProviderSession.Connect(root, new ThrowingContentProvider());
            CloudPlaceholderCreationResult placeholder = root.CreateFilePlaceholder(
                "failure.bin",
                4096,
                "failure.bin"u8);

            await Assert.ThrowsAnyAsync<IOException>(async () =>
            {
                _ = await File.ReadAllBytesAsync(placeholder.Path).WaitAsync(TimeSpan.FromSeconds(5));
            });

            await session.DisposeAsync();
            session = null;
            root.Unregister();
            registered = false;
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            TryUnregister(root, registered);
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task SessionShutdownCancelsActiveContentRequest()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string rootPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        CloudSyncRoot? root = null;
        CloudProviderSession? session = null;
        bool registered = false;
        Directory.CreateDirectory(rootPath);

        try
        {
            root = RegisterRoot(rootPath, "Cancellation");
            registered = true;
            BlockingContentProvider provider = new();
            session = CloudProviderSession.Connect(root, provider);
            CloudPlaceholderCreationResult placeholder = root.CreateFilePlaceholder(
                "cancel.bin",
                4096,
                "cancel.bin"u8);

            Task<byte[]> readTask = File.ReadAllBytesAsync(placeholder.Path);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.DisposeAsync();
            session = null;

            await provider.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<IOException>(async () =>
            {
                _ = await readTask.WaitAsync(TimeSpan.FromSeconds(5));
            });

            root.Unregister();
            registered = false;
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            TryUnregister(root, registered);
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static CloudSyncRoot RegisterRoot(string rootPath, string scenario)
    {
        Guid providerId = Guid.NewGuid();
        SyncRootRegistrationOptions options =
            SyncRootRegistrationOptions.CreateBuilder(
                    $"CfSharp {scenario} {providerId:N}",
                    "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
                .WithPopulationPolicy(CloudPopulationPolicy.AlwaysFull)
                .WithRootMarkedInSync()
                .Build();
        return CloudSyncRoot.Register(rootPath, options);
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static void TryUnregister(CloudSyncRoot? root, bool registered)
    {
        if (!registered || root is null)
        {
            return;
        }

        try
        {
            root.Unregister();
        }
        catch (CloudFilesException)
        {
            // Preserve the original failure while still attempting system cleanup.
        }
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
            string path = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
            if (!path.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The placeholder identity escapes the content root.");
            }

            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return ValueTask.FromResult(stream);
        }
    }

    private sealed class ThrowingContentProvider : ICloudFileContentProvider
    {
        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Injected provider failure.");
        }
    }

    private sealed class BlockingContentProvider : ICloudFileContentProvider
    {
        internal TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("The blocking provider unexpectedly resumed.");
        }
    }
}
