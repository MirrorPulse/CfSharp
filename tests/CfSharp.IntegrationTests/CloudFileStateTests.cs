using System.Runtime.Versioning;

using CfSharp.Storage.Sqlite;

namespace CfSharp.IntegrationTests;

public sealed class CloudFileStateTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task StateAvailabilityAndRangesRoundTripThroughManagedOperations()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string testPath = Path.Combine(
            Path.GetTempPath(),
            "CfSharp-file-state-tests",
            Guid.NewGuid().ToString("N"));
        string rootPath = Path.Combine(testPath, "root");
        string databasePath = Path.Combine(testPath, "state", "cfsharp.db");
        byte[] content = new byte[192 * 1024];
        new Random(8192).NextBytes(content);
        Guid providerId = Guid.NewGuid();
        CloudFileSystem? fileSystem = null;
        CloudSyncRoot? root = null;
        bool registered = false;
        Directory.CreateDirectory(rootPath);

        try
        {
            SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
                .CreateBuilder($"CfSharp File State {providerId:N}", "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
                .WithPopulationPolicy(CloudPopulationPolicy.Partial)
                .WithRootMarkedInSync()
                .Build();
            fileSystem = CloudFileSystem.CreateBuilder(rootPath)
                .WithStateStore(new SqliteCloudStateStoreFactory(databasePath))
                .WithRegistration(registration)
                .WithContentProvider(new ContentProvider(content))
                .Build();
            await fileSystem.StartAsync();
            root = CloudSyncRoot.Open(rootPath);
            registered = true;

            await fileSystem.Root.CreatePlaceholdersAsync(
            [
                CloudFilePlaceholderSpec.CreateBuilder(
                    "state.bin",
                    "remote-state",
                    content.Length).Build(),
                CloudFilePlaceholderSpec.CreateBuilder(
                        "always-full.bin",
                        "remote-always-full",
                        content.Length)
                    .WithInitialAvailability(CloudAvailabilityTarget.AlwaysAvailable)
                    .Build(),
            ]);
            CloudFile file = fileSystem.GetFile("state.bin");

            CloudStateChangeResult pinned = await file.SetPinStateAsync(CloudPinTarget.Pinned);
            Assert.Equal(CloudPinState.Pinned, pinned.Snapshot.PinState);
            Assert.Null(pinned.OperationUsn);
            CloudStateChangeResult unpinned = await file.SetPinStateAsync(CloudPinTarget.Unpinned);
            Assert.NotEqual(CloudPinState.Pinned, unpinned.Snapshot.PinState);

            CloudStateChangeResult outOfSync = await file.SetInSyncAsync(inSync: false);
            Assert.Equal(CloudSynchronizationState.NotInSync, outOfSync.Snapshot.SynchronizationState);
            CloudStateChangeResult inSync = await file.SetInSyncAsync(inSync: true);
            Assert.Equal(CloudSynchronizationState.InSync, inSync.Snapshot.SynchronizationState);

            CloudFileRange firstPage = new(0, Environment.SystemPageSize);
            await file.HydrateAsync(firstPage);
            IReadOnlyList<CloudFileRange> onDisk = await file.GetRangesAsync(
                CloudPlaceholderRangeKind.OnDisk,
                CloudFileRange.WholeFile);
            IReadOnlyList<CloudFileRange> validated = await file.GetRangesAsync(
                CloudPlaceholderRangeKind.Validated,
                CloudFileRange.WholeFile);
            IReadOnlyList<CloudFileRange> modifiedBeforeWrite = await file.GetRangesAsync(
                CloudPlaceholderRangeKind.Modified,
                CloudFileRange.WholeFile);
            AssertRangeCovered(onDisk, firstPage);
            AssertRangeCovered(validated, firstPage);
            Assert.Empty(modifiedBeforeWrite);

            await file.DehydrateAsync(firstPage);
            IReadOnlyList<CloudFileRange> afterPartialDehydration = await file.GetRangesAsync(
                CloudPlaceholderRangeKind.OnDisk,
                firstPage);
            Assert.Empty(afterPartialDehydration);

            CloudAvailabilityChangeResult local = await file.SetAvailabilityAsync(
                CloudAvailabilityTarget.LocallyAvailable);
            Assert.True(local.PinStateApplied);
            Assert.True(local.ContentStateApplied);
            Assert.NotEqual(CloudPinState.Pinned, local.Snapshot.PinState);
            Assert.Equal(CloudContentAvailability.FullyAvailable, local.Snapshot.ContentAvailability);
            Assert.Equal(content, await File.ReadAllBytesAsync(file.FullPath));

            await using (FileStream stream = new(
                file.FullPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.Read))
            {
                stream.Position = Environment.SystemPageSize;
                await stream.WriteAsync(new byte[] { 0xA5 });
            }

            IReadOnlyList<CloudFileRange> modified = await file.GetRangesAsync(
                CloudPlaceholderRangeKind.Modified,
                CloudFileRange.WholeFile);
            Assert.NotEmpty(modified);

            await file.SetInSyncAsync(inSync: true);
            CloudAvailabilityChangeResult always = await file.SetAvailabilityAsync(
                CloudAvailabilityTarget.AlwaysAvailable);
            Assert.Equal(CloudPinState.Pinned, always.Snapshot.PinState);
            Assert.Equal(CloudContentAvailability.FullyAvailable, always.Snapshot.ContentAvailability);

            CloudAvailabilityChangeResult online = await file.SetAvailabilityAsync(
                CloudAvailabilityTarget.OnlineOnly);
            Assert.NotEqual(CloudPinState.Pinned, online.Snapshot.PinState);
            Assert.Equal(CloudContentAvailability.OnlineOnly, online.Snapshot.ContentAvailability);
            Assert.Empty(await file.GetRangesAsync(
                CloudPlaceholderRangeKind.OnDisk,
                CloudFileRange.WholeFile));

            CloudFile alwaysFull = fileSystem.GetFile("always-full.bin");
            CloudAvailabilityTransitionException failure = await Assert.ThrowsAsync<
                CloudAvailabilityTransitionException>(() => alwaysFull
                    .SetAvailabilityAsync(CloudAvailabilityTarget.OnlineOnly)
                    .AsTask());
            Assert.Equal("CloudFile.Dehydrate", failure.Failure.Operation);
            Assert.True(failure.PartialResult.PinStateApplied);
            Assert.False(failure.PartialResult.ContentStateApplied);
            Assert.Equal(alwaysFull.FullPath, failure.PartialResult.Path);
            Assert.Equal(
                CloudContentAvailability.FullyAvailable,
                failure.PartialResult.Snapshot.ContentAvailability);
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
                    // Preserve the original test failure while still cleaning persistent state.
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
                    // No registration was created, or it was already removed successfully.
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
                    // Preserve the original test failure while still attempting system cleanup.
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
                    // A preceding cleanup failure is more useful than a secondary lock error.
                }
            }
        }
    }

    private static void AssertRangeCovered(
        IReadOnlyList<CloudFileRange> actual,
        CloudFileRange expected)
    {
        Assert.Contains(actual, range =>
            range.Offset <= expected.Offset &&
            checked(range.Offset + range.Length) >= checked(expected.Offset + expected.Length));
    }

    private sealed class ContentProvider : ICloudFileContentProvider
    {
        private readonly byte[] _content;

        internal ContentProvider(byte[] content)
        {
            _content = content;
        }

        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new MemoryStream(_content, writable: false));
        }
    }
}
