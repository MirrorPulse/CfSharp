using System.Runtime.Versioning;

namespace CfSharp;

internal interface ICloudFileSystemRuntime
{
    ICloudFileSystemRuntimeSession Start(
        string syncRootPath,
        SyncRootRegistrationOptions? registration,
        ICloudFileContentProvider? contentProvider);
}

internal interface ICloudFileSystemRuntimeSession : IAsyncDisposable
{
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
        ICloudFileContentProvider? contentProvider)
    {
        CloudSyncRoot syncRoot = registration is null
            ? CloudSyncRoot.Open(syncRootPath)
            : CloudSyncRoot.Register(syncRootPath, registration);
        CloudProviderSession? providerSession = contentProvider is null
            ? null
            : CloudProviderSession.Connect(syncRoot, contentProvider);
        return new WindowsCloudFileSystemRuntimeSession(providerSession);
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private sealed class WindowsCloudFileSystemRuntimeSession : ICloudFileSystemRuntimeSession
    {
        private readonly CloudProviderSession? _providerSession;

        internal WindowsCloudFileSystemRuntimeSession(CloudProviderSession? providerSession)
        {
            _providerSession = providerSession;
        }

        public ValueTask DisposeAsync() =>
            _providerSession?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
