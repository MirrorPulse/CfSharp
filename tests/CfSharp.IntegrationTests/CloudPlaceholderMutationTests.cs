using System.Runtime.Versioning;

using CfSharp.Storage.Sqlite;

namespace CfSharp.IntegrationTests;

public sealed class CloudPlaceholderMutationTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task ConvertUpdatePopulationIdentityRemovalAndRevertRemainCoordinated()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string testPath = Path.Combine(
            Path.GetTempPath(),
            "CfSharp-placeholder-mutation-tests",
            Guid.NewGuid().ToString("N"));
        string rootPath = Path.Combine(testPath, "root");
        string databasePath = Path.Combine(testPath, "state", "cfsharp.db");
        Guid providerId = Guid.NewGuid();
        CloudFileSystem? fileSystem = null;
        CloudSyncRoot? root = null;
        bool registered = false;
        byte[] content = "ordinary content retained across conversion and revert"u8.ToArray();
        Directory.CreateDirectory(rootPath);

        try
        {
            string filePath = Path.Combine(rootPath, "ordinary.bin");
            string directoryPath = Path.Combine(rootPath, "Folder");
            await File.WriteAllBytesAsync(filePath, content);
            Directory.CreateDirectory(directoryPath);
            SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
                .CreateBuilder($"CfSharp Mutation Test {providerId:N}", "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
                .WithPopulationPolicy(CloudPopulationPolicy.Partial)
                .WithRootMarkedInSync()
                .Build();
            fileSystem = CloudFileSystem.CreateBuilder(rootPath)
                .WithStateStore(new SqliteCloudStateStoreFactory(databasePath))
                .WithRegistration(registration)
                .WithContentProvider(new UnexpectedContentProvider())
                .Build();
            await fileSystem.StartAsync();
            root = CloudSyncRoot.Open(rootPath);
            registered = true;

            CloudFile file = fileSystem.GetFile("ordinary.bin");
            CloudPlaceholderIdentity initialIdentity = new(
                Guid.NewGuid(),
                "remote-ordinary",
                "revision-1");
            CloudPlaceholderMutationResult converted = await file.ConvertToPlaceholderAsync(
                initialIdentity,
                CloudPlaceholderConversionOptions.CreateBuilder()
                    .WithInSyncState()
                    .Build());

            Assert.NotNull(converted.OperationUsn);
            Assert.True(converted.DurableStateUpdated);
            Assert.True(converted.Snapshot.IsPlaceholder);
            Assert.Equal(initialIdentity.ItemId, converted.Snapshot.ItemId);
            Assert.Equal(CloudSynchronizationState.InSync, converted.Snapshot.SynchronizationState);
            Assert.Equal(content, await File.ReadAllBytesAsync(filePath));

            CloudPlaceholderIdentity updatedIdentity = new(
                initialIdentity.ItemId,
                initialIdentity.RemoteId,
                "revision-2");
            DateTimeOffset lastWrite = DateTimeOffset.UtcNow.AddMinutes(-5);
            CloudPlaceholderPatch patch = CloudPlaceholderPatch.CreateBuilder()
                .WithIdentity(updatedIdentity)
                .WithMetadata(CloudPlaceholderMetadata.CreateFileBuilder()
                    .WithLastWriteTime(lastWrite)
                    .Build())
                .WithInSyncVerification()
                .WithInSyncState(inSync: false)
                .Build();
            CloudPlaceholderMutationResult updated = await file.UpdatePlaceholderAsync(patch);

            Assert.True(updated.DurableStateUpdated);
            Assert.Equal(updatedIdentity.RemoteRevision, updated.Snapshot.RemoteRevision);
            Assert.Equal(CloudSynchronizationState.NotInSync, updated.Snapshot.SynchronizationState);
            Assert.True(updated.Snapshot.PlaceholderIdentity.Span.SequenceEqual(
                updatedIdentity.Encode()));
            Assert.Equal(content.Length, updated.Snapshot.Length);
            Assert.Equal(lastWrite.ToFileTime(), updated.Snapshot.LastWriteTime?.ToFileTime());
            await Assert.ThrowsAsync<ArgumentException>(() => file
                .UpdatePlaceholderAsync(CloudPlaceholderPatch.CreateBuilder()
                    .WithPopulationState(CloudDirectoryPopulationState.Complete)
                    .Build())
                .AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() => file
                .UpdatePlaceholderAsync(CloudPlaceholderPatch.CreateBuilder()
                    .WithDehydratedRanges([new CloudFileRange(1, Environment.SystemPageSize)])
                    .Build())
                .AsTask());

            CloudFilePlaceholderSpec conditionalSpec = CloudFilePlaceholderSpec
                .CreateBuilder("conditional.bin", "remote-conditional", 0)
                .Build();
            CloudPlaceholderBatchEntryResult conditionalEntry = await fileSystem.Root
                .CreatePlaceholderAsync(conditionalSpec);
            Assert.True(conditionalEntry.CreateUsn > 0);
            CloudFile conditionalFile = fileSystem.GetFile("conditional.bin");
            CloudPlaceholderPatch stalePatch = CloudPlaceholderPatch.CreateBuilder()
                .WithInSyncState(inSync: false)
                .WithExpectedUsn(conditionalEntry.CreateUsn!.Value)
                .Build();
            await Assert.ThrowsAsync<CloudFilesException>(() => conditionalFile
                .UpdatePlaceholderAsync(stalePatch)
                .AsTask());

            CloudDirectory directory = fileSystem.GetDirectory("Folder");
            CloudPlaceholderMutationResult convertedDirectory =
                await directory.ConvertToPlaceholderAsync(
                    new CloudPlaceholderIdentity(Guid.NewGuid(), "remote-folder"),
                    CloudPlaceholderConversionOptions.CreateBuilder()
                        .WithInSyncState()
                        .WithPopulationState(CloudDirectoryPopulationState.Partial)
                        .Build());
            Assert.True(convertedDirectory.Snapshot.PlaceholderState.HasFlag(
                CloudPlaceholderState.Partial));
            await Assert.ThrowsAsync<ArgumentException>(() => directory
                .UpdatePlaceholderAsync(CloudPlaceholderPatch.CreateBuilder()
                    .WithContentMode(CloudFileContentMode.AlwaysFull)
                    .Build())
                .AsTask());
            CloudPlaceholderMutationResult complete = await directory
                .SetPopulationStateAsync(CloudDirectoryPopulationState.Complete);
            Assert.False(complete.Snapshot.PlaceholderState.HasFlag(
                CloudPlaceholderState.Partial));
            Assert.False(complete.DurableStateUpdated);

            CloudPlaceholderMutationResult identityRemoved = await directory
                .UpdatePlaceholderAsync(
                    CloudPlaceholderPatch.CreateBuilder().WithIdentityRemoval().Build());
            Assert.True(identityRemoved.Snapshot.IsPlaceholder);
            Assert.Empty(identityRemoved.Snapshot.PlaceholderIdentity.ToArray());
            Assert.Null(identityRemoved.Snapshot.ItemId);
            Assert.True(identityRemoved.DurableStateUpdated);

            CloudPlaceholderMutationResult reverted = await file.RevertToRegularItemAsync();
            Assert.False(reverted.Snapshot.IsPlaceholder);
            Assert.Null(reverted.Snapshot.ItemId);
            Assert.True(reverted.DurableStateUpdated);
            Assert.Equal(content, await File.ReadAllBytesAsync(filePath));
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
                Directory.Delete(testPath, recursive: true);
            }
        }
    }

    private sealed class UnexpectedContentProvider : ICloudFileContentProvider
    {
        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"The full placeholder '{request.NormalizedPath}' should not request content.");
    }
}
