using System.Runtime.Versioning;

namespace CfSharp.IntegrationTests;

public sealed class ManagedSyncRootLifecycleTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public void RegisterUpdateOpenAndUnregisterHaveDeterministicLifecycle()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string rootPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        Guid providerId = Guid.NewGuid();
        byte[] identity = providerId.ToByteArray();
        CloudSyncRoot? root = null;
        bool registered = false;

        Directory.CreateDirectory(rootPath);

        try
        {
            SyncRootRegistrationOptions initialOptions =
                SyncRootRegistrationOptions.CreateBuilder(
                        $"CfSharp Managed {providerId:N}",
                        "1.0.0-test")
                    .WithProviderId(providerId)
                    .WithSyncRootIdentity(identity)
                    .WithHydrationPolicy(
                        CloudHydrationPolicy.Progressive,
                        CloudHydrationPolicyModifiers.AutoDehydrationAllowed)
                    .WithPopulationPolicy(CloudPopulationPolicy.Partial)
                    .WithInSyncPolicy(CloudInSyncPolicy.TrackAll)
                    .WithRootMarkedInSync()
                    .Build();

            root = CloudSyncRoot.Register(rootPath, initialOptions);
            registered = true;

            CloudSyncRootInfo initialInfo = root.GetInfo();
            Assert.Equal(Path.GetFullPath(rootPath), root.Path);
            Assert.Equal(initialOptions.ProviderName, initialInfo.ProviderName);
            Assert.Equal("1.0.0-test", initialInfo.ProviderVersion);
            Assert.True(initialInfo.SyncRootIdentity.SequenceEqual(identity));
            Assert.Equal(CloudHydrationPolicy.Progressive, initialInfo.HydrationPolicy);
            Assert.Equal(
                CloudHydrationPolicyModifiers.AutoDehydrationAllowed,
                initialInfo.HydrationModifiers);
            Assert.Equal(CloudPopulationPolicy.Partial, initialInfo.PopulationPolicy);
            Assert.Equal(CloudInSyncPolicy.TrackAll, initialInfo.InSyncPolicy);
            Assert.Equal(CloudProviderStatus.Disconnected, initialInfo.ProviderStatus);

            CloudSyncRoot repeatedRoot = CloudSyncRoot.Register(rootPath, initialOptions);
            Assert.Equal(initialInfo.FileId, repeatedRoot.GetInfo().FileId);
            Assert.Equal(initialInfo.ProviderVersion, repeatedRoot.GetInfo().ProviderVersion);

            SyncRootRegistrationOptions updateOptions =
                SyncRootRegistrationOptions.CreateBuilder(initialOptions.ProviderName, "2.0.0-test")
                    .WithProviderId(providerId)
                    .WithSyncRootIdentity(identity)
                    .WithHydrationPolicy(CloudHydrationPolicy.Full)
                    .WithPopulationPolicy(CloudPopulationPolicy.Full)
                    .WithInSyncPolicy(CloudInSyncPolicy.TrackFileAll)
                    .WithExistingRegistrationUpdate()
                    .Build();

            root = CloudSyncRoot.Register(rootPath, updateOptions);
            CloudSyncRootInfo updatedInfo = root.GetInfo();
            Assert.Equal("2.0.0-test", updatedInfo.ProviderVersion);
            Assert.Equal(CloudHydrationPolicy.Full, updatedInfo.HydrationPolicy);
            Assert.Equal(CloudPopulationPolicy.Full, updatedInfo.PopulationPolicy);
            Assert.Equal(CloudInSyncPolicy.TrackFileAll, updatedInfo.InSyncPolicy);

            CloudSyncRoot openedRoot = CloudSyncRoot.Open(rootPath);
            Assert.Equal(root.Path, openedRoot.Path);
            Assert.Equal(updatedInfo.FileId, openedRoot.GetInfo().FileId);

            root.Unregister();
            registered = false;

            CloudFilesException queryException =
                Assert.Throws<CloudFilesException>(() => root.GetInfo());
            Assert.Equal("CloudSyncRoot.GetInfo", queryException.Operation);
            Assert.Equal(root.Path, queryException.Path);

            CloudFilesException unregisterException =
                Assert.Throws<CloudFilesException>(() => root.Unregister());
            Assert.Equal("CloudSyncRoot.Unregister", unregisterException.Operation);
            Assert.Equal(root.Path, unregisterException.Path);
        }
        finally
        {
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
}
