using System.Runtime.Versioning;

using CfSharp.Storage.Sqlite;

namespace CfSharp.IntegrationTests;

public sealed class CloudPlaceholderBatchTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task BatchCreationPersistsIdentitySupportsRetryAndReportsPartialFailure()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string testPath = Path.Combine(
            Path.GetTempPath(),
            "CfSharp-placeholder-batch-tests",
            Guid.NewGuid().ToString("N"));
        string rootPath = Path.Combine(testPath, "root");
        string databasePath = Path.Combine(testPath, "state", "cfsharp.db");
        Guid providerId = Guid.NewGuid();
        CloudFileSystem? fileSystem = null;
        CloudSyncRoot? root = null;
        bool registered = false;
        byte[] locallyAvailableContent = "locally available content"u8.ToArray();
        byte[] alwaysAvailableContent = "always available content"u8.ToArray();
        Directory.CreateDirectory(rootPath);

        try
        {
            SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
                .CreateBuilder($"CfSharp Placeholder Test {providerId:N}", "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
                .WithPopulationPolicy(CloudPopulationPolicy.Partial)
                .WithRootMarkedInSync()
                .Build();
            fileSystem = CloudFileSystem.CreateBuilder(rootPath)
                .WithStateStore(new SqliteCloudStateStoreFactory(databasePath))
                .WithRegistration(registration)
                .WithContentProvider(new TestContentProvider(
                    new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["remote-local"] = locallyAvailableContent,
                        ["remote-always"] = alwaysAvailableContent,
                    }))
                .Build();
            await fileSystem.StartAsync();
            root = CloudSyncRoot.Open(rootPath);
            registered = true;

            CloudFilePlaceholderSpec file = CloudFilePlaceholderSpec
                .CreateBuilder("report.bin", "remote-report", 4096)
                .WithRemoteRevision("revision-1")
                .Build();
            CloudDirectoryPlaceholderSpec directory = CloudDirectoryPlaceholderSpec
                .CreateBuilder("Archive", "remote-archive")
                .WithPopulationState(CloudDirectoryPopulationState.Complete)
                .Build();
            CloudFilePlaceholderSpec locallyAvailable = CloudFilePlaceholderSpec
                .CreateBuilder("local.bin", "remote-local", locallyAvailableContent.Length)
                .WithInitialAvailability(CloudAvailabilityTarget.LocallyAvailable)
                .Build();
            CloudFilePlaceholderSpec alwaysAvailable = CloudFilePlaceholderSpec
                .CreateBuilder("always.bin", "remote-always", alwaysAvailableContent.Length)
                .WithInitialAvailability(CloudAvailabilityTarget.AlwaysAvailable)
                .Build();

            CloudPlaceholderBatchResult created = await fileSystem.Root
                .CreatePlaceholdersAsync([file, directory, locallyAvailable, alwaysAvailable]);

            Assert.True(
                created.IsSuccessful,
                $"Batch={created.BatchError?.HResult:X8}; " +
                string.Join(
                    "; ",
                    created.Entries.Select(entry =>
                        $"{entry.Specification.Name}:{entry.Status}:" +
                        $"{entry.Progress}:{entry.Error?.Operation}:{entry.Error?.HResult:X8}")));
            Assert.Equal(4, created.SucceededCount);
            Assert.All(created.Entries, entry =>
            {
                Assert.True(entry.Progress.HasFlag(
                    CloudPlaceholderCreationProgress.PlaceholderCreated));
                Assert.True(entry.DurableStatePersisted);
                Assert.NotNull(entry.CreateUsn);
                Assert.NotNull(entry.Item);
            });

            CloudItemSnapshot fileSnapshot = await fileSystem.GetFile("report.bin").InspectAsync();
            CloudItemSnapshot directorySnapshot = await fileSystem
                .GetDirectory("Archive")
                .InspectAsync();
            Assert.True(
                fileSnapshot.IsPlaceholder,
                $"State={fileSnapshot.PlaceholderState}; Attributes={fileSnapshot.Attributes}; " +
                $"IdentityLength={fileSnapshot.PlaceholderIdentity.Length}");
            Assert.Equal(file.Identity.ItemId, fileSnapshot.ItemId);
            Assert.Equal(file.Identity.RemoteId, fileSnapshot.RemoteId);
            Assert.Equal(file.Identity.RemoteRevision, fileSnapshot.RemoteRevision);
            Assert.True(fileSnapshot.PlaceholderIdentity.Span.SequenceEqual(file.Identity.Encode()));
            Assert.True(
                directorySnapshot.IsPlaceholder,
                $"State={directorySnapshot.PlaceholderState}; Attributes={directorySnapshot.Attributes}; " +
                $"IdentityLength={directorySnapshot.PlaceholderIdentity.Length}");
            Assert.Equal(directory.Identity.ItemId, directorySnapshot.ItemId);
            Assert.False(directorySnapshot.PlaceholderState.HasFlag(CloudPlaceholderState.Partial));
            CloudPlaceholderBatchEntryResult localResult = created.Entries[2];
            CloudPlaceholderBatchEntryResult alwaysResult = created.Entries[3];
            Assert.True(localResult.Progress.HasFlag(
                CloudPlaceholderCreationProgress.PinStateApplied));
            Assert.True(localResult.Progress.HasFlag(
                CloudPlaceholderCreationProgress.ContentHydrated));
            Assert.True(alwaysResult.Progress.HasFlag(
                CloudPlaceholderCreationProgress.PinStateApplied));
            Assert.True(alwaysResult.Progress.HasFlag(
                CloudPlaceholderCreationProgress.ContentHydrated));
            Assert.Equal(
                locallyAvailableContent,
                await File.ReadAllBytesAsync(Path.Combine(rootPath, "local.bin")));
            Assert.Equal(
                alwaysAvailableContent,
                await File.ReadAllBytesAsync(Path.Combine(rootPath, "always.bin")));
            Assert.NotEqual(
                CloudPinState.Pinned,
                (await fileSystem.GetFile("local.bin").InspectAsync()).PinState);
            Assert.Equal(
                CloudPinState.Pinned,
                (await fileSystem.GetFile("always.bin").InspectAsync()).PinState);

            CloudPlaceholderBatchResult retried = await fileSystem.Root
                .CreatePlaceholdersAsync([file, directory, locallyAvailable, alwaysAvailable]);
            Assert.True(retried.IsSuccessful);
            Assert.All(retried.Entries, entry =>
            {
                Assert.True(entry.Progress.HasFlag(
                    CloudPlaceholderCreationProgress.ExistingPlaceholderMatched));
                Assert.True(entry.DurableStatePersisted);
                Assert.Null(entry.CreateUsn);
            });
            await Assert.ThrowsAsync<ArgumentException>(() => fileSystem.Root
                .CreatePlaceholdersAsync([file, file])
                .AsTask());
            CloudPlaceholderIdentity duplicateRemoteIdentity = new(
                Guid.NewGuid(),
                file.Identity.RemoteId);
            await Assert.ThrowsAsync<ArgumentException>(() => fileSystem.Root
                .CreatePlaceholdersAsync(
                    [
                        file,
                        CloudFilePlaceholderSpec.CreateBuilder(
                            "duplicate-remote.bin",
                            duplicateRemoteIdentity,
                            1).Build(),
                    ])
                .AsTask());
            Assert.False(File.Exists(Path.Combine(rootPath, "duplicate-remote.bin")));

            await File.WriteAllTextAsync(Path.Combine(rootPath, "replace.txt"), "ordinary");
            CloudFilePlaceholderSpec replacement = CloudFilePlaceholderSpec
                .CreateBuilder("replace.txt", "remote-replacement", 24)
                .WithCollisionBehavior(CloudPlaceholderCollisionBehavior.Supersede)
                .Build();
            CloudPlaceholderBatchEntryResult replaced = await fileSystem.Root
                .CreatePlaceholderAsync(replacement);
            Assert.Equal(CloudItemOperationStatus.Succeeded, replaced.Status);
            CloudItemSnapshot replacementSnapshot = await fileSystem
                .GetFile("replace.txt")
                .InspectAsync();
            Assert.True(replacementSnapshot.IsPlaceholder);
            Assert.Equal(replacement.Identity.ItemId, replacementSnapshot.ItemId);
            Assert.Equal(24, replacementSnapshot.Length);

            await File.WriteAllTextAsync(Path.Combine(rootPath, "collision.txt"), "ordinary");
            CloudFilePlaceholderSpec collision = CloudFilePlaceholderSpec
                .CreateBuilder("collision.txt", "remote-collision", 8)
                .Build();
            CloudFilePlaceholderSpec continued = CloudFilePlaceholderSpec
                .CreateBuilder("continued.bin", "remote-continued", 16)
                .Build();
            CloudPlaceholderBatchResult partial = await fileSystem.Root
                .CreatePlaceholdersAsync([collision, continued]);

            Assert.False(partial.IsSuccessful);
            Assert.Equal(CloudItemOperationStatus.Failed, partial.Entries[0].Status);
            Assert.IsType<CloudFilesException>(partial.Entries[0].Error);
            Assert.Equal(CloudItemOperationStatus.Succeeded, partial.Entries[1].Status);
            Assert.True(File.Exists(Path.Combine(rootPath, "continued.bin")));
            Assert.Throws<AggregateException>(partial.ThrowIfAnyFailed);
            await Assert.ThrowsAsync<CloudFilesException>(() => fileSystem.Root
                .CreatePlaceholderAsync(collision)
                .AsTask());

            await File.WriteAllTextAsync(Path.Combine(rootPath, "stop.txt"), "ordinary");
            CloudPlaceholderBatchResult stopped = await fileSystem.Root.CreatePlaceholdersAsync(
                [
                    CloudFilePlaceholderSpec.CreateBuilder("stop.txt", "remote-stop", 4).Build(),
                    CloudFilePlaceholderSpec.CreateBuilder("not-created.bin", "remote-later", 4).Build(),
                ],
                new CloudPlaceholderBatchOptions(stopOnFirstFailure: true));
            Assert.Equal(CloudItemOperationStatus.Failed, stopped.Entries[0].Status);
            Assert.Equal(CloudItemOperationStatus.NotProcessed, stopped.Entries[1].Status);
            Assert.False(File.Exists(Path.Combine(rootPath, "not-created.bin")));
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

    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task PersistenceFailureRetainsAppliedResultAndRetryMatchesIdentity()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string rootPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        Guid providerId = Guid.NewGuid();
        CloudFileSystem? fileSystem = null;
        CloudSyncRoot? root = null;
        bool registered = false;
        Directory.CreateDirectory(rootPath);

        try
        {
            SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
                .CreateBuilder($"CfSharp Persistence Failure {providerId:N}", "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
                .WithPopulationPolicy(CloudPopulationPolicy.Partial)
                .WithRootMarkedInSync()
                .Build();
            fileSystem = CloudFileSystem.CreateBuilder(rootPath)
                .WithStateStore(new FailingStoreFactory())
                .WithRegistration(registration)
                .WithContentProvider(new TestContentProvider(
                    new Dictionary<string, byte[]>(StringComparer.Ordinal)))
                .Build();
            await fileSystem.StartAsync();
            root = CloudSyncRoot.Open(rootPath);
            registered = true;
            CloudFilePlaceholderSpec specification = CloudFilePlaceholderSpec
                .CreateBuilder("recoverable.bin", "remote-recoverable", 128)
                .Build();

            CloudPlaceholderPersistenceException first = await Assert.ThrowsAsync<
                CloudPlaceholderPersistenceException>(() => fileSystem.Root
                    .CreatePlaceholdersAsync([specification])
                    .AsTask());
            CloudPlaceholderBatchEntryResult created = Assert.Single(
                first.AppliedResult.Entries);
            Assert.Equal(CloudItemOperationStatus.Succeeded, created.Status);
            Assert.True(created.Progress.HasFlag(
                CloudPlaceholderCreationProgress.PlaceholderCreated));
            Assert.False(created.DurableStatePersisted);
            Assert.True(File.Exists(Path.Combine(rootPath, "recoverable.bin")));

            CloudPlaceholderPersistenceException retry = await Assert.ThrowsAsync<
                CloudPlaceholderPersistenceException>(() => fileSystem.Root
                    .CreatePlaceholdersAsync([specification])
                    .AsTask());
            CloudPlaceholderBatchEntryResult matched = Assert.Single(
                retry.AppliedResult.Entries);
            Assert.True(matched.Progress.HasFlag(
                CloudPlaceholderCreationProgress.ExistingPlaceholderMatched));
            Assert.False(matched.DurableStatePersisted);
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

            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private sealed class TestContentProvider : ICloudFileContentProvider
    {
        private readonly IReadOnlyDictionary<string, byte[]> _content;

        internal TestContentProvider(IReadOnlyDictionary<string, byte[]> content)
        {
            _content = content;
        }

        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloudPlaceholderIdentity identity = CloudPlaceholderIdentity.Decode(
                request.FileIdentity);
            if (!_content.TryGetValue(identity.RemoteId, out byte[]? content))
            {
                throw new InvalidOperationException(
                    $"The online-only placeholder '{request.NormalizedPath}' should not hydrate.");
            }

            return ValueTask.FromResult<Stream>(new MemoryStream(content, writable: false));
        }
    }

    private sealed class FailingStoreFactory : ICloudStateStoreFactory
    {
        public ValueTask<ICloudStateStore> OpenAsync(
            CloudStateStoreContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ICloudStateStore>(new FailingStore());
        }
    }

    private sealed class FailingStore : ICloudStateStore
    {
        public ValueTask<ICloudStateTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ICloudStateTransaction>(new FailingTransaction());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingTransaction : ICloudStateTransaction, ICloudItemStateRepository
    {
        public ICloudItemStateRepository Items => this;

        public ICloudCheckpointRepository Checkpoints => throw new NotSupportedException();

        public ICloudOperationJournal Operations => throw new NotSupportedException();

        public ICloudConflictRepository Conflicts => throw new NotSupportedException();

        public ICloudRemoteBatchRepository RemoteBatches => throw new NotSupportedException();

        public ICloudEchoSuppressionRepository EchoSuppressions => throw new NotSupportedException();

        public ValueTask<CloudItemState?> GetByItemIdAsync(
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<CloudItemState?>(null);

        public ValueTask<CloudItemState?> GetByRemoteIdAsync(
            string remoteId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<CloudItemState?>(null);

        public ValueTask<CloudItemState?> GetByRelativePathAsync(
            string relativePath,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<CloudItemState?>(null);

        public ValueTask<IReadOnlyList<CloudItemState>> ListSubtreeAsync(
            string relativePath,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<CloudItemState>>([]);

        public ValueTask UpsertAsync(
            CloudItemState item,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("Injected durable-state failure."));

        public ValueTask RemoveAsync(
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask CommitAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
