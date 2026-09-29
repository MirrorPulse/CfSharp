using System.Runtime.Versioning;

using CfSharp.Storage.Sqlite;

namespace CfSharp.IntegrationTests;

public sealed class RemoteContentUpdateTests
{
    [Theory]
    [InlineData("new-data", false)]
    [InlineData("longer replacement data", false)]
    [InlineData("", false)]
    [InlineData("new-data", true)]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task UpsertInvalidatesOldContentOrPreservesPinnedRevision(string replacement, bool pinned)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string area = Path.Combine(Path.GetTempPath(), "CfSharp-remote-content", Guid.NewGuid().ToString("N"));
        string root = Path.Combine(area, "root");
        Directory.CreateDirectory(root);
        bool registered = false;
        try
        {
            await using CloudFileSystem fileSystem = CloudFileSystem.CreateBuilder(root)
                .WithStateStore(new SqliteCloudStateStoreFactory(Path.Combine(area, "state.db")))
                .WithRegistration(SyncRootRegistrationOptions.CreateBuilder("CfSharp Content Test", "1.0")
                    .WithProviderId(Guid.NewGuid()).WithRootMarkedInSync().Build())
                .WithContentProvider(new ContentProvider(replacement))
                .Build();
            await fileSystem.StartAsync();
            registered = true;
            string path = Path.Combine(root, "file.txt");
            await File.WriteAllTextAsync(path, "old-data");
            CloudFile file = fileSystem.GetFile("file.txt");
            await file.ConvertToPlaceholderAsync(new CloudPlaceholderIdentity(Guid.NewGuid(), "file", "r1"));
            await file.SetInSyncAsync(true);
            if (pinned)
            {
                await file.SetPinStateAsync(CloudPinTarget.Pinned);
            }

            CloudRemoteChange change = new("update", CloudRemoteChangeKind.FileUpsert, "file", "r2",
                CloudItemKind.File, "file.txt", previousRemoteRevision: "r1", length: replacement.Length,
                metadata: CloudPlaceholderMetadata.CreateFileBuilder().Build());
            CloudRemoteApplyResult result = await fileSystem.ApplyRemoteChangesAsync(
                new CloudRemoteChangeBatch("batch", Array.Empty<byte>(), [change], new byte[] { 1 }));
            CloudRemoteApplyEntryResult entry = Assert.Single(result.Entries);
            if (pinned)
            {
                Assert.Equal(CloudRemoteApplyEntryStatus.Failed, entry.Status);
                Assert.IsType<CloudFilesException>(entry.Error);
                Assert.Equal("r1", (await file.InspectAsync()).RemoteRevision);
                Assert.Equal("old-data", await File.ReadAllTextAsync(path));
                Assert.Equal(CloudPinState.Pinned, (await file.InspectAsync()).PinState);
            }
            else
            {
                Assert.Equal(CloudRemoteApplyEntryStatus.Applied, entry.Status);
                Assert.Equal("r2", (await file.InspectAsync()).RemoteRevision);
                Assert.Equal(replacement, await File.ReadAllTextAsync(path));
            }
        }
        finally
        {
            if (registered)
            {
                CloudSyncRoot.Open(root).Unregister();
            }

            Directory.Delete(area, recursive: true);
        }
    }

    private sealed class ContentProvider(string content) : ICloudFileContentProvider
    {
        public ValueTask<Stream> OpenReadAsync(CloudFileFetchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)));
        }
    }
}
