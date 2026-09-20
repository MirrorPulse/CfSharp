using System.Runtime.Versioning;

using CfSharp.Native;

namespace CfSharp.IntegrationTests;

public sealed class PlaceholderCreationTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public unsafe void CreateOnlineOnlyPlaceholderAppliesExpectedMetadata()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string rootPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        const string relativeName = "online-only.bin";
        const long fileSize = 8192;
        byte[] identity = Guid.NewGuid().ToByteArray();
        CloudSyncRoot? root = null;
        bool registered = false;

        Directory.CreateDirectory(rootPath);

        try
        {
            Guid providerId = Guid.NewGuid();
            SyncRootRegistrationOptions options =
                SyncRootRegistrationOptions.CreateBuilder(
                        $"CfSharp Placeholder {providerId:N}",
                        "1.0.0-test")
                    .WithProviderId(providerId)
                    .WithSyncRootIdentity(providerId.ToByteArray())
                    .WithPopulationPolicy(CloudPopulationPolicy.AlwaysFull)
                    .WithRootMarkedInSync()
                    .Build();

            root = CloudSyncRoot.Register(rootPath, options);
            registered = true;

            fixed (char* rootPathPointer = rootPath)
            fixed (char* relativeNamePointer = relativeName)
            fixed (byte* identityPointer = identity)
            {
                CfPlaceholderCreateInfo placeholder = new()
                {
                    RelativeFileName = relativeNamePointer,
                    FsMetadata = new CfFsMetadata
                    {
                        BasicInfo = new CfFileBasicInfo
                        {
                            FileAttributes = (uint)FileAttributes.Normal,
                        },
                        FileSize = fileSize,
                    },
                    FileIdentity = identityPointer,
                    FileIdentityLength = (uint)identity.Length,
                    Flags = CfPlaceholderCreateFlags.MarkInSync,
                };

                uint entriesProcessed;
                int result = CfApi.CfCreatePlaceholders(
                    rootPathPointer,
                    &placeholder,
                    1,
                    CfCreateFlags.StopOnError,
                    &entriesProcessed);

                Assert.Equal(0, result);
                Assert.Equal(1u, entriesProcessed);
                Assert.Equal(0, placeholder.Result);
                Assert.NotEqual(0, placeholder.CreateUsn);
            }

            string placeholderPath = Path.Combine(rootPath, relativeName);
            FileInfo placeholderFile = new(placeholderPath);
            Assert.True(placeholderFile.Exists);
            Assert.Equal(fileSize, placeholderFile.Length);

            VerifyProtectedHandleAndTransferKey(placeholderPath);

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

    [SupportedOSPlatform("windows10.0.16299")]
    private static unsafe void VerifyProtectedHandleAndTransferKey(string placeholderPath)
    {
        nint protectedHandle = 0;
        bool referenced = false;

        fixed (char* placeholderPathPointer = placeholderPath)
        {
            int openResult = CfApi.CfOpenFileWithOplock(
                placeholderPathPointer,
                CfOpenFileFlags.Foreground,
                out protectedHandle);
            Assert.Equal(0, openResult);
            Assert.NotEqual(0, protectedHandle);
        }

        try
        {
            referenced = CfApi.CfReferenceProtectedHandle(protectedHandle) != 0;
            Assert.True(referenced);

            nint win32Handle = CfApi.CfGetWin32HandleFromProtectedHandle(protectedHandle);
            Assert.NotEqual(0, win32Handle);
            Assert.NotEqual(-1, win32Handle);

            CfTransferKey transferKey = default;
            int keyResult = CfApi.CfGetTransferKey(win32Handle, &transferKey);
            Assert.Equal(0, keyResult);
            CfApi.CfReleaseTransferKey(win32Handle, &transferKey);
            Assert.NotEqual(0, transferKey.Internal);
        }
        finally
        {
            if (referenced)
            {
                CfApi.CfReleaseProtectedHandle(protectedHandle);
            }

            CfApi.CfCloseHandle(protectedHandle);
        }
    }
}
