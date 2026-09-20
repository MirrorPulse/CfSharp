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

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
            {
                ReportAndClearSyncRootStatus(rootPath);
            }

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

            int progressResult = CfApi.CfReportProviderProgress(
                connectionKey,
                default,
                100,
                50);
            Assert.True(progressResult < 0);

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                int progress2Result = CfApi.CfReportProviderProgress2(
                    connectionKey,
                    default,
                    default,
                    100,
                    50,
                    0);
                Assert.True(progress2Result < 0);
            }

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

    [SupportedOSPlatform("windows10.0.17134")]
    private static unsafe void ReportAndClearSyncRootStatus(string rootPath)
    {
        const string description = "CfSharp native reporting integration test.";
        int descriptionLength = checked((description.Length + 1) * sizeof(char));
        int bufferLength = checked(sizeof(CfSyncStatus) + descriptionLength);
        byte* buffer = stackalloc byte[bufferLength];
        new Span<byte>(buffer, bufferLength).Clear();

        CfSyncStatus* status = (CfSyncStatus*)buffer;
        status->StructSize = checked((uint)bufferLength);
        status->Code = 0x80000001;
        status->DescriptionOffset = (uint)sizeof(CfSyncStatus);
        status->DescriptionLength = checked((uint)descriptionLength);

        description.AsSpan().CopyTo(new Span<char>(
            (char*)(buffer + status->DescriptionOffset),
            description.Length));

        fixed (char* rootPathPointer = rootPath)
        {
            int reportResult = CfApi.CfReportSyncStatus(rootPathPointer, status);
            Assert.Equal(0, reportResult);

            int clearResult = CfApi.CfReportSyncStatus(rootPathPointer, null);
            Assert.Equal(0, clearResult);
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
