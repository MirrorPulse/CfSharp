using System.Runtime.Versioning;

using CfSharp.Native;

namespace CfSharp.IntegrationTests;

public sealed class ProtectedHandleLifetimeTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public void SafeProtectedHandleOwnsReferenceAndCloseLifetimes()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string rootPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        string filePath = Path.Combine(rootPath, "ordinary.bin");
        CloudSyncRoot? root = null;
        bool registered = false;
        Directory.CreateDirectory(rootPath);
        File.WriteAllBytes(filePath, [1, 2, 3, 4]);

        try
        {
            Guid providerId = Guid.NewGuid();
            SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
                .CreateBuilder($"CfSharp Handle {providerId:N}", "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithPopulationPolicy(CloudPopulationPolicy.AlwaysFull)
                .WithRootMarkedInSync()
                .Build();
            root = CloudSyncRoot.Register(rootPath, registration);
            registered = true;

            using SafeCloudFilesProtectedHandle handle = SafeCloudFilesProtectedHandle.Open(
                filePath,
                CfOpenFileFlags.Foreground | CfOpenFileFlags.WriteAccess,
                "ProtectedHandleLifetimeTests.Open");
            SafeCloudFilesProtectedHandle.CloudFilesHandleReference reference =
                handle.AcquireReference();

            Assert.NotEqual(0, reference.Win32Handle);
            Assert.NotEqual(-1, reference.Win32Handle);
            reference.Dispose();
            reference.Dispose();
            handle.Dispose();
            handle.Dispose();

            root.Unregister();
            registered = false;
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
                    // Preserve the original failure while still attempting system cleanup.
                }
            }

            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
