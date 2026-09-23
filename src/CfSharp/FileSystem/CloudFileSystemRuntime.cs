using System.Runtime.Versioning;

namespace CfSharp;

internal interface ICloudFileSystemRuntime
{
    ICloudFileSystemRuntimeSession Start(
        string syncRootPath,
        SyncRootRegistrationOptions? registration,
        ICloudFileContentProvider? contentProvider,
        ICloudStateStore stateStore);
}

internal interface ICloudFileSystemRuntimeSession : IAsyncDisposable
{
}

/// <summary>Optional runtime capability used by safe provider-initiated item leases.</summary>
internal interface ICloudTransferRuntimeSession
{
    CloudProviderSession ProviderSession { get; }
}

[SupportedOSPlatform("windows10.0.16299")]
internal sealed class WindowsCloudFileSystemRuntime : ICloudFileSystemRuntime
{
    internal static WindowsCloudFileSystemRuntime Instance { get; } = new();

    private WindowsCloudFileSystemRuntime()
    {
    }

    public ICloudFileSystemRuntimeSession Start(
        string syncRootPath,
        SyncRootRegistrationOptions? registration,
        ICloudFileContentProvider? contentProvider,
        ICloudStateStore stateStore)
    {
        CloudSyncRoot syncRoot = registration is null
            ? CloudSyncRoot.Open(syncRootPath)
            : CloudSyncRoot.Register(syncRootPath, registration);
        CloudProviderSession? providerSession = contentProvider is null
            ? null
            : CloudProviderSession.Connect(syncRoot, contentProvider, stateStore);
        return new WindowsCloudFileSystemRuntimeSession(providerSession);
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private sealed class WindowsCloudFileSystemRuntimeSession :
        ICloudFileSystemRuntimeSession,
        ICloudTransferRuntimeSession
    {
        private readonly CloudProviderSession? _providerSession;

        internal WindowsCloudFileSystemRuntimeSession(CloudProviderSession? providerSession)
        {
            _providerSession = providerSession;
        }

        CloudProviderSession ICloudTransferRuntimeSession.ProviderSession =>
            _providerSession ?? throw new InvalidOperationException(
                "A content provider session is required for provider-initiated transfers.");

        public ValueTask DisposeAsync() =>
            _providerSession?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
