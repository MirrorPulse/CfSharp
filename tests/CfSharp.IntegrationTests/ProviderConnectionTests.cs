using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using CfSharp.Native;

namespace CfSharp.IntegrationTests;

public sealed class ProviderConnectionTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public unsafe void ConnectQueryStatusAndDisconnectHaveDeterministicLifetime()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string rootPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        CloudSyncRoot? root = null;
        CfConnectionKey connectionKey = default;
        bool connected = false;
        bool registered = false;

        Directory.CreateDirectory(rootPath);

        try
        {
            Guid providerId = Guid.NewGuid();
            SyncRootRegistrationOptions options =
                SyncRootRegistrationOptions.CreateBuilder(
                        $"CfSharp Connection {providerId:N}",
                        "1.0.0-test")
                    .WithProviderId(providerId)
                    .WithSyncRootIdentity(providerId.ToByteArray())
                    .WithPopulationPolicy(CloudPopulationPolicy.AlwaysFull)
                    .WithRootMarkedInSync()
                    .Build();

            root = CloudSyncRoot.Register(rootPath, options);
            registered = true;

            CfCallbackRegistration* callbackTable = stackalloc CfCallbackRegistration[2];
            callbackTable[0] = new CfCallbackRegistration
            {
                Type = CfCallbackType.FetchData,
                Callback = &IgnoreFetchData,
            };
            callbackTable[1] = new CfCallbackRegistration
            {
                Type = CfCallbackType.None,
                Callback = null,
            };

            fixed (char* rootPathPointer = rootPath)
            {
                int connectResult = CfApi.CfConnectSyncRoot(
                    rootPathPointer,
                    callbackTable,
                    null,
                    CfConnectFlags.None,
                    out connectionKey);

                Assert.Equal(0, connectResult);
                Assert.NotEqual(0, connectionKey.Internal);
                connected = true;
            }

            int updateStatusResult = CfApi.CfUpdateSyncProviderStatus(
                connectionKey,
                CfSyncProviderStatus.SyncIncremental);
            Assert.Equal(0, updateStatusResult);

            int queryStatusResult = CfApi.CfQuerySyncProviderStatus(
                connectionKey,
                out CfSyncProviderStatus providerStatus);
            Assert.Equal(0, queryStatusResult);
            Assert.True(providerStatus.HasFlag(CfSyncProviderStatus.SyncIncremental));

            CloudFilesException connectedUnregisterException =
                Assert.Throws<CloudFilesException>(() => root.Unregister());
            Assert.Equal("CloudSyncRoot.Unregister", connectedUnregisterException.Operation);

            int disconnectResult = CfApi.CfDisconnectSyncRoot(connectionKey);
            Assert.Equal(0, disconnectResult);
            connected = false;

            int repeatedDisconnectResult = CfApi.CfDisconnectSyncRoot(connectionKey);
            Assert.True(repeatedDisconnectResult < 0);

            root.Unregister();
            registered = false;
        }
        finally
        {
            if (connected)
            {
                _ = CfApi.CfDisconnectSyncRoot(connectionKey);
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

            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static unsafe void IgnoreFetchData(
        CfCallbackInfo* callbackInfo,
        CfCallbackParameters* callbackParameters)
    {
        _ = callbackInfo;
        _ = callbackParameters;
    }
}
