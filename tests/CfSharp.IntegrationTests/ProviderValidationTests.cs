using System.Collections.Concurrent;
using System.Runtime.Versioning;

namespace CfSharp.IntegrationTests;

public sealed class ProviderValidationTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task ValidationCallbackAcknowledgesAcceptedAndRejectsChangedRanges()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string testPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        string rootPath = Path.Combine(testPath, "root");
        Directory.CreateDirectory(rootPath);
        byte[] content = new byte[96 * 1024];
        new Random(8128).NextBytes(content);
        Guid providerId = Guid.NewGuid();
        ValidationProvider provider = new(content);
        CloudProviderSession? session = null;
        CloudSyncRoot? root = null;
        bool registered = false;

        try
        {
            SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
                .CreateBuilder($"CfSharp Validation {providerId:N}", "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithHydrationPolicy(
                    CloudHydrationPolicy.Progressive,
                    CloudHydrationPolicyModifiers.ValidationRequired)
                .WithPopulationPolicy(CloudPopulationPolicy.AlwaysFull)
                .WithRootMarkedInSync()
                .Build();
            root = CloudSyncRoot.Register(rootPath, registration);
            registered = true;
            session = CloudProviderSession.Connect(root, provider);

            CloudPlaceholderCreationResult accepted = root.CreateFilePlaceholder(
                "accepted.bin",
                content.Length,
                "accepted.bin"u8);
            Assert.Equal(content, await File.ReadAllBytesAsync(accepted.Path));
            CloudProviderValidateDataRequest acceptedRequest =
                await provider.Validations["accepted.bin"].Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(acceptedRequest.IsExplicitHydration);
            Assert.Equal(content.Length, acceptedRequest.FileSize);

            CloudPlaceholderCreationResult rejected = root.CreateFilePlaceholder(
                "rejected.bin",
                content.Length,
                "rejected.bin"u8);
            await Assert.ThrowsAnyAsync<IOException>(async () =>
            {
                _ = await File.ReadAllBytesAsync(rejected.Path).WaitAsync(TimeSpan.FromSeconds(10));
            });
            CloudProviderValidateDataRequest rejectedRequest =
                await provider.Validations["rejected.bin"].Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.EndsWith(
                "rejected.bin",
                rejectedRequest.NormalizedPath,
                StringComparison.OrdinalIgnoreCase);

            await session.DisposeAsync();
            session = null;
            root.Unregister();
            registered = false;
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync();
                }
                catch (Exception)
                {
                    // Preserve the original test failure while still attempting cleanup.
                }
            }

            if (registered && root is not null)
            {
                try
                {
                    root.Unregister();
                }
                catch (CloudFilesException)
                {
                    // Preserve the original failure while still attempting cleanup.
                }
            }

            if (Directory.Exists(testPath))
            {
                Directory.Delete(testPath, recursive: true);
            }
        }
    }

    private sealed class ValidationProvider : ICloudDemandProvider
    {
        private readonly byte[] _content;

        internal ValidationProvider(byte[] content)
        {
            _content = content;
        }

        internal ConcurrentDictionary<
            string,
            TaskCompletionSource<CloudProviderValidateDataRequest>>
            Validations
        { get; } = new(
                StringComparer.OrdinalIgnoreCase);

        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new MemoryStream(_content, writable: false));
        }

        public ValueTask<CloudProviderValidationResult> ValidateDataAsync(
            CloudProviderValidateDataRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(request.NormalizedPath);
            Validations.GetOrAdd(
                    name,
                    static _ => new TaskCompletionSource<CloudProviderValidateDataRequest>(
                        TaskCreationOptions.RunContinuationsAsynchronously))
                .TrySetResult(request);
            return ValueTask.FromResult(
                name.Equals("rejected.bin", StringComparison.OrdinalIgnoreCase)
                    ? CloudProviderValidationResult.Rejected()
                    : CloudProviderValidationResult.Accepted());
        }
    }
}
