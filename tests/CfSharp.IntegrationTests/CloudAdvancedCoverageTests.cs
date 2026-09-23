using System.Runtime.Versioning;

using CfSharp.Storage.Sqlite;

namespace CfSharp.IntegrationTests;

public sealed class CloudAdvancedCoverageTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.17134")]
    public async Task ManagedLeaseTransferAndRichStatusRoundTripOnWindows()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
        {
            return;
        }

        string testPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        string rootPath = Path.Combine(testPath, "root");
        string databasePath = Path.Combine(testPath, "state", "cfsharp.db");
        Guid providerId = Guid.NewGuid();
        CloudFileSystem? fileSystem = null;
        CloudSyncRoot? root = null;
        bool registered = false;
        Directory.CreateDirectory(rootPath);

        try
        {
            SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
                .CreateBuilder($"CfSharp Advanced {providerId:N}", "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithHydrationPolicy(
                    CloudHydrationPolicy.Progressive,
                    CloudHydrationPolicyModifiers.ValidationRequired)
                .WithPopulationPolicy(CloudPopulationPolicy.Partial)
                .WithRootMarkedInSync()
                .Build();

            fileSystem = CloudFileSystem.CreateBuilder(rootPath)
                .WithStateStore(new SqliteCloudStateStoreFactory(databasePath))
                .WithRegistration(registration)
                .WithContentProvider(new UnusedContentProvider())
                .Build();
            await fileSystem.StartAsync();
            root = CloudSyncRoot.Open(rootPath);
            registered = true;

            root.ReportStatus(new CloudSyncStatus
            {
                Code = 0x80000042,
                Description = "Phase 9 status ✓",
                DeviceId = new byte[] { 0x01, 0x80, 0xFF },
            });
            root.ClearStatus();

            CloudFilePlaceholderSpec specification = CloudFilePlaceholderSpec
                .CreateBuilder("transfer.bin", "phase9-transfer", 4096)
                .WithInSyncState(false)
                .Build();
            CloudPlaceholderBatchResult creation = await fileSystem.Root
                .CreatePlaceholdersAsync([specification]);
            creation.ThrowIfAnyFailed();

            CloudFile file = fileSystem.GetFile("transfer.bin");
            CloudCorrelationVector requestedVector = CloudCorrelationVector.Create(1, "phase9-test");
            {
                await using CloudItemLease lease = await file.AcquireLeaseAsync(
                    new CloudItemLeaseOptions
                    {
                        Access = CloudItemLeaseAccess.Read | CloudItemLeaseAccess.Write,
                        Foreground = true,
                    });
                CloudCorrelationVector? initialVector = lease.GetCorrelationVector();
                if (initialVector is not null)
                {
                    lease.SetCorrelationVector(initialVector.Value);
                    Assert.Equal(initialVector, lease.GetCorrelationVector());
                }

                await using CloudTransfer transfer = await lease.BeginTransferAsync(
                    new CloudTransferOptions { CorrelationVector = requestedVector });
                Assert.Same(lease, transfer.Lease);
                Assert.False(lease.IsInvalidated);

                byte[] content = new byte[4096];
                new Random(9).NextBytes(content);
                await transfer.TransferDataAsync(0, content);
                byte[] retrieved = new byte[4096];
                CloudTransferReadResult readResult = await transfer.RetrieveDataAsync(0, retrieved);
                Assert.Equal(content.Length, readResult.BytesRead);
                Assert.Equal(content, retrieved);
                await transfer.AcknowledgeDataAsync(0, content.Length);
            }

            await fileSystem.DisposeAsync();
            fileSystem = null;
            root.Unregister();
            registered = false;
        }
        finally
        {
            if (fileSystem is not null)
            {
                try
                {
                    await fileSystem.DisposeAsync();
                }
                catch (Exception)
                {
                    // Preserve the original failure while attempting cleanup.
                }
            }

            if (!registered)
            {
                try
                {
                    root = CloudSyncRoot.Open(rootPath);
                    registered = true;
                }
                catch (CloudFilesException)
                {
                    // Registration was never created or was already removed.
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
                    // Preserve the original failure while attempting cleanup.
                }
            }

            if (Directory.Exists(testPath))
            {
                try
                {
                    Directory.Delete(testPath, recursive: true);
                }
                catch (IOException)
                {
                    // A preceding failure is more useful than a secondary cleanup error.
                }
            }
        }
    }

    private sealed class UnusedContentProvider : ICloudFileContentProvider
    {
        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException("The proactive transfer test does not hydrate through callbacks.");
        }
    }
}
