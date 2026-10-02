using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

using CfSharp.Native;
using CfSharp.Storage.Sqlite;

using Microsoft.Win32.SafeHandles;

using Xunit.Abstractions;

namespace CfSharp.IntegrationTests;

public sealed partial class CloudItemUsnTests
{
    private readonly ITestOutputHelper _output;

    public CloudItemUsnTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(CloudInSyncPolicy.TrackAll)]
    [InlineData(CloudInSyncPolicy.None)]
    [SupportedOSPlatform("windows10.0.16299")]
    public Task ReadUsnObservesOrdinaryAndPlaceholderItemsWithoutHydration(CloudInSyncPolicy policy) =>
        WithFileSystemAsync(policy, async (fileSystem, rootPath) =>
        {
            CloudFile file = fileSystem.GetFile("ordinary.bin");
            Assert.True(await file.ReadUsnAsync() > 0);
            Assert.True(await fileSystem.GetDirectory("Folder").ReadUsnAsync() > 0);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fileSystem
                .GetDirectory("ordinary.bin").ReadUsnAsync().AsTask());
            using CancellationTokenSource canceled = new();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.ReadUsnAsync(canceled.Token).AsTask());
            CloudFilesException missing = await Assert.ThrowsAsync<CloudFilesException>(() => fileSystem
                .GetFile("missing.bin").ReadUsnAsync().AsTask());
            Assert.Equal("CloudItem.ReadUsn", missing.Operation);
            Assert.Equal(2, missing.Win32ErrorCode);
            Assert.Equal(unchecked((int)0x80070002), missing.HResult);
            Assert.Equal(Path.Combine(rootPath, "missing.bin"), missing.Path);

            CloudPlaceholderMutationResult converted = await file.ConvertToPlaceholderAsync(
                new CloudPlaceholderIdentity(Guid.NewGuid(), "remote-usn"));
            _output.WriteLine($"ConvertUsn={converted.OperationUsn}; ObservedUsn={await file.ReadUsnAsync()}");
            Assert.True(await file.ReadUsnAsync() > 0);
            Assert.Equal(OriginalContent, await File.ReadAllBytesAsync(file.FullPath));

            long beforeWriteUsn = await file.ReadUsnAsync();
            await ChangeContentAsync(file.FullPath);
            Assert.NotEqual(beforeWriteUsn, await file.ReadUsnAsync());
            CloudPlaceholderMutationResult patched = await file.UpdatePlaceholderAsync(
                CloudPlaceholderPatch.CreateBuilder()
                    .WithMetadata(CloudPlaceholderMetadata.CreateFileBuilder()
                        .WithLastWriteTime(DateTimeOffset.UtcNow.AddMinutes(-5)).Build())
                    .Build());
            _output.WriteLine($"MetadataPatchUsn={patched.OperationUsn}; ObservedUsn={await file.ReadUsnAsync()}");
            Assert.True(await file.ReadUsnAsync() > 0);

            CloudDirectory directory = fileSystem.GetDirectory("Folder");
            await directory.ConvertToPlaceholderAsync(new CloudPlaceholderIdentity(Guid.NewGuid(), "remote-folder"));
            Assert.True(await directory.ReadUsnAsync() > 0);
            await fileSystem.Root.CreatePlaceholderAsync(CloudFilePlaceholderSpec
                .CreateBuilder("online-only.bin", "remote-online-only", 4096).Build());
            CloudFile onlineOnly = fileSystem.GetFile("online-only.bin");
            Assert.True(await onlineOnly.ReadUsnAsync() > 0);
            Assert.Equal(CloudContentAvailability.OnlineOnly, (await onlineOnly.InspectAsync()).ContentAvailability);
            await file.RevertToRegularItemAsync();
            Assert.True(await file.ReadUsnAsync() > 0);
            await fileSystem.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => file.ReadUsnAsync().AsTask());
        });

    [Theory]
    [InlineData(CloudInSyncPolicy.TrackAll)]
    [InlineData(CloudInSyncPolicy.None)]
    [SupportedOSPlatform("windows10.0.16299")]
    public Task ChangedContentRejectsThePreviouslyObservedUsn(CloudInSyncPolicy policy) =>
        WithFileSystemAsync(policy, async (fileSystem, _) =>
        {
            CloudFile file = fileSystem.GetFile("ordinary.bin");
            await file.ConvertToPlaceholderAsync(new CloudPlaceholderIdentity(Guid.NewGuid(), "remote-usn"));
            long staleUsn = await file.ReadUsnAsync();
            Assert.True(staleUsn > 0);
            await ChangeContentAsync(file.FullPath);
            Assert.NotEqual(staleUsn, await file.ReadUsnAsync());
            CloudStateChangeResult cleared = await file.SetInSyncAsync(false);
            _output.WriteLine($"ChangedClearUsn={cleared.OperationUsn}");
            CloudFilesException stale = await Assert.ThrowsAsync<CloudFilesException>(() => file
                .SetInSyncAsync(true, new CloudInSyncChangeOptions(staleUsn)).AsTask());
            Assert.Equal("CloudItem.SetInSync", stale.Operation);
            Assert.Equal(unchecked((int)0x80070179), stale.HResult);
            Assert.Equal(CloudSynchronizationState.NotInSync, (await file.InspectAsync()).SynchronizationState);
        });

    [Theory]
    [InlineData(CloudInSyncPolicy.TrackAll)]
    [InlineData(CloudInSyncPolicy.None)]
    [SupportedOSPlatform("windows10.0.16299")]
    public Task CurrentUsnMustSupportVerifiedConditionalInSync(CloudInSyncPolicy policy) =>
        WithFileSystemAsync(policy, async (fileSystem, rootPath) =>
        {
            CloudFile file = fileSystem.GetFile("ordinary.bin");
            CloudPlaceholderMutationResult converted = await file.ConvertToPlaceholderAsync(
                new CloudPlaceholderIdentity(Guid.NewGuid(), "remote-usn"));
            CloudStateChangeResult cleared = await file.SetInSyncAsync(false);
            (int clearHresult, long clearUsn) = SetInSyncNative(file.FullPath, false, 0);
            _output.WriteLine($"ConvertUsn={converted.OperationUsn}; ClearUsn={cleared.OperationUsn}; NativeClearHRESULT=0x{clearHresult:X8}; NativeClearUsn={clearUsn}");
            Assert.Equal(0, clearHresult);

            string nativeConvertPath = Path.Combine(rootPath, "native-convert.bin");
            await File.WriteAllBytesAsync(nativeConvertPath, OriginalContent);
            (int convertHresult, long convertUsn) = ConvertNative(nativeConvertPath);
            _output.WriteLine($"NativeConvertHRESULT=0x{convertHresult:X8}; NativeConvertUsn={convertUsn}; ObservedUsn={await fileSystem.GetFile("native-convert.bin").ReadUsnAsync()}");
            Assert.Equal(0, convertHresult);

            // Keep the native comparisons in the failing acceptance fixture. They distinguish
            // a platform rejection from managed marshalling, policies, or an open/close race.
            long nativeToken = await file.ReadUsnAsync();
            (int nativeHresult, long nativeUsn) = SetInSyncNative(file.FullPath, true, nativeToken);
            _output.WriteLine($"NativeConditionalHRESULT=0x{nativeHresult:X8}; InputUsn={nativeToken}; OutputUsn={nativeUsn}");
            foreach (uint access in new uint[] { 0x00040080, 0x00000082 })
            {
                long token = await file.ReadUsnAsync();
                (int hresult, long usn) = SetInSyncMetadataNative(file.FullPath, token, access);
                _output.WriteLine($"Access=0x{access:X8}; MetadataConditionalHRESULT=0x{hresult:X8}; InputUsn={token}; OutputUsn={usn}");
            }

            foreach (uint flags in new uint[] { 0, 0x00200000 })
            {
                (int hresult, long usn) = SetInSyncSameHandleNative(file.FullPath, flags);
                _output.WriteLine($"Flags=0x{flags:X8}; SameHandleConditionalHRESULT=0x{hresult:X8}; Usn={usn}");
            }

            long verifiedUsn = await file.ReadUsnAsync();
            Assert.True(verifiedUsn > 0);
            await using (FileStream stream = File.OpenRead(file.FullPath))
            {
                Assert.Equal(SHA256.HashData(OriginalContent), await SHA256.HashDataAsync(stream));
            }

            long afterHashUsn = await file.ReadUsnAsync();
            _output.WriteLine($"BeforeHashUsn={verifiedUsn}; AfterHashUsn={afterHashUsn}");
            Assert.Equal(verifiedUsn, afterHashUsn);
            // This is a required acceptance assertion, not a capability skip. CF-002 remains
            // unresolved if Windows rejects this unchanged positive token. Never retry with zero.
            CloudStateChangeResult marked = await file.SetInSyncAsync(true, new CloudInSyncChangeOptions(verifiedUsn));
            _output.WriteLine($"ConditionalMarkUsn={marked.OperationUsn}");
            Assert.Equal(CloudSynchronizationState.InSync, marked.Snapshot.SynchronizationState);
        });

    private static byte[] OriginalContent => "CF-002 ordinary complete file"u8.ToArray();

    private static async Task ChangeContentAsync(string path)
    {
        // Mutate the existing placeholder. CREATE_ALWAYS may replace its reparse identity;
        // that would test item replacement rather than rejection of a stale content token.
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        await stream.WriteAsync("X"u8.ToArray());
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private async Task WithFileSystemAsync(
        CloudInSyncPolicy policy,
        Func<CloudFileSystem, string, Task> verify)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string testPath = Path.Combine(Path.GetTempPath(), "CfSharp-usn-tests", Guid.NewGuid().ToString("N"));
        string rootPath = Path.Combine(testPath, "root");
        Directory.CreateDirectory(rootPath);
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "ordinary.bin"), OriginalContent);
        Directory.CreateDirectory(Path.Combine(rootPath, "Folder"));
        Guid providerId = Guid.NewGuid();
        SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
            .CreateBuilder($"CfSharp USN Test {providerId:N}", "1.0.0-test")
            .WithProviderId(providerId)
            .WithHydrationPolicy(CloudHydrationPolicy.Progressive)
            .WithInSyncPolicy(policy)
            .Build();
        CloudFileSystem fileSystem = CloudFileSystem.CreateBuilder(rootPath)
            .WithStateStore(new SqliteCloudStateStoreFactory(Path.Combine(testPath, "state.db")))
            .WithRegistration(registration)
            .WithContentProvider(new UnexpectedContentProvider())
            .Build();
        bool started = false;
        try
        {
            await fileSystem.StartAsync();
            started = true;
            _output.WriteLine($"OS={Environment.OSVersion.Version}; Architecture={RuntimeInformation.ProcessArchitecture}; Policy={policy}");
            await verify(fileSystem, rootPath);
        }
        finally
        {
            await fileSystem.DisposeAsync();
            if (started)
            {
                CloudSyncRoot.Open(rootPath).Unregister();
            }

            Directory.Delete(testPath, recursive: true);
        }
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static unsafe (int HResult, long Usn) ConvertNative(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        byte[] identity = new CloudPlaceholderIdentity(Guid.NewGuid(), "native-usn").Encode();
        fixed (byte* identityPointer = identity)
        {
            long usn = 0;
            int hresult = CfApi.CfConvertToPlaceholder(handle.DangerousGetHandle(), identityPointer,
                checked((uint)identity.Length), CfConvertFlags.None, &usn, null);
            return (hresult, usn);
        }
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static unsafe (int HResult, long Usn) SetInSyncNative(string path, bool inSync, long expectedUsn)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        long usn = expectedUsn;
        int hresult = CfApi.CfSetInSyncState(handle.DangerousGetHandle(),
            inSync ? CfInSyncState.InSync : CfInSyncState.NotInSync, CfSetInSyncFlags.None, &usn);
        return (hresult, usn);
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static unsafe (int HResult, long Usn) SetInSyncMetadataNative(string path, long expectedUsn, uint access)
    {
        using SafeFileHandle handle = CreateFile(path, access, 7, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        }

        long usn = expectedUsn;
        int hresult = CfApi.CfSetInSyncState(handle.DangerousGetHandle(), CfInSyncState.InSync, CfSetInSyncFlags.None, &usn);
        return (hresult, usn);
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static unsafe (int HResult, long Usn) SetInSyncSameHandleNative(string path, uint flags)
    {
        using SafeFileHandle handle = CreateFile(path, 0x00040080, 7, 0, 3, flags, 0);
        if (handle.IsInvalid)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        }

        byte* buffer = stackalloc byte[4096];
        // No version input requests v2. Read on this very handle, then pass the returned USN
        // directly to CfSetInSyncState without a managed query or a handle close in between.
        if (!DeviceIoControl(handle, 0x000900eb, null, 0, buffer, 4096, out uint written, 0))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        }

        Assert.True(written >= 60);
        Assert.Equal(2, *(ushort*)(buffer + 4));
        long usn = *(long*)(buffer + 24);
        Assert.True(usn > 0);
        int hresult = CfApi.CfSetInSyncState(handle.DangerousGetHandle(), CfInSyncState.InSync, CfSetInSyncFlags.None, &usn);
        return (hresult, usn);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle handle,
        uint code, void* input, uint inputLength, void* output, uint outputLength, out uint written, nint overlapped);

    private sealed class UnexpectedContentProvider : ICloudFileContentProvider
    {
        public ValueTask<Stream> OpenReadAsync(CloudFileFetchRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Reading the USN must not request content.");
    }
}
