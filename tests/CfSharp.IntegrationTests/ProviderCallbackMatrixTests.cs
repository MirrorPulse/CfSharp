using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;

using CfSharp.Native;

namespace CfSharp.IntegrationTests;

public sealed class ProviderCallbackMatrixTests
{
    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public async Task FileLifecycleRoutesApprovalsAndCompletionNotifications()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string testPath = Path.Combine(Path.GetTempPath(), $"CfSharp-{Guid.NewGuid():N}");
        string rootPath = Path.Combine(testPath, "root");
        Directory.CreateDirectory(rootPath);
        Guid providerId = Guid.NewGuid();
        byte[] content = new byte[128 * 1024];
        new Random(8192).NextBytes(content);
        RecordingDemandProvider provider = new(content);
        CloudProviderSession? session = null;
        CloudSyncRoot? root = null;
        bool registered = false;

        try
        {
            SyncRootRegistrationOptions registration = SyncRootRegistrationOptions
                .CreateBuilder($"CfSharp Callback Matrix {providerId:N}", "1.0.0-test")
                .WithProviderId(providerId)
                .WithSyncRootIdentity(providerId.ToByteArray())
                .WithHydrationPolicy(
                    CloudHydrationPolicy.Progressive,
                    CloudHydrationPolicyModifiers.AutoDehydrationAllowed)
                .WithPopulationPolicy(CloudPopulationPolicy.Partial)
                .WithRootMarkedInSync()
                .Build();
            root = CloudSyncRoot.Register(rootPath, registration);
            registered = true;
            session = CloudProviderSession.Connect(root, provider);

            CloudPlaceholderCreationResult placeholder = root.CreateFilePlaceholder(
                "callback.bin",
                content.Length,
                "callback.bin"u8);
            using (Process readProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"[IO.File]::ReadAllBytes('{placeholder.Path}') | Out-Null\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            })!)
            {
                await readProcess.WaitForExitAsync();
            }

            Assert.Equal(content, await File.ReadAllBytesAsync(placeholder.Path));
            await provider.WaitForNotificationAsync(
                CloudProviderNotificationKind.FileOpenCompleted);
            await provider.WaitForNotificationAsync(
                CloudProviderNotificationKind.FileCloseCompleted);

            session.DispatchPolicyCallbackForTesting(
                CloudProviderRequestKind.Dehydrate,
                "\\callback.bin",
                isBackground: true);
            CloudProviderDehydrateRequest dehydrateRequest =
                await provider.DehydrateApproval.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(dehydrateRequest.IsBackground);

            session.DispatchPolicyCallbackForTesting(
                CloudProviderRequestKind.Delete,
                "\\callback.bin",
                isUndelete: true);
            CloudProviderDeleteRequest deleteRequest =
                await provider.DeleteApproval.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(deleteRequest.IsUndelete);

            session.DispatchPolicyCallbackForTesting(
                CloudProviderRequestKind.Rename,
                "\\callback.bin",
                "\\renamed.bin");
            CloudProviderRenameRequest renameRequest =
                await provider.RenameApproval.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(renameRequest.SourceInScope);
            Assert.True(renameRequest.TargetInScope);

            session.SetDirectoryContinuationForTesting("\\callback.bin", "stale-source");
            session.SetDirectoryContinuationForTesting("\\callback.bin\\child", "stale-child");
            session.SetDirectoryContinuationForTesting("\\renamed.bin", "stale-target");

            session.DispatchCompletionCallbackForTesting(
                CloudProviderNotificationKind.DehydrateCompleted,
                "\\callback.bin",
                flags: 2);
            session.DispatchCompletionCallbackForTesting(
                CloudProviderNotificationKind.DeleteCompleted,
                "\\callback.bin",
                flags: 3);
            session.DispatchCompletionCallbackForTesting(
                CloudProviderNotificationKind.RenameCompleted,
                "\\renamed.bin",
                "\\callback.bin",
                flags: 4);

            CloudProviderCompletionNotification dehydrateCompletion =
                await provider.WaitForNotificationAsync(
                    CloudProviderNotificationKind.DehydrateCompleted);
            CloudProviderCompletionNotification deleteCompletion =
                await provider.WaitForNotificationAsync(
                    CloudProviderNotificationKind.DeleteCompleted);
            CloudProviderCompletionNotification renameCompletion =
                await provider.WaitForNotificationAsync(
                    CloudProviderNotificationKind.RenameCompleted);
            Assert.Equal(2u, dehydrateCompletion.Flags);
            Assert.Equal(3u, deleteCompletion.Flags);
            Assert.Equal("\\callback.bin", renameCompletion.RelatedPath);
            Assert.Equal(4u, renameCompletion.Flags);
            Assert.False(session.HasDirectoryContinuationForTesting("\\callback.bin"));
            Assert.False(session.HasDirectoryContinuationForTesting("\\callback.bin\\child"));
            Assert.False(session.HasDirectoryContinuationForTesting("\\renamed.bin"));

            await session.DisposeAsync();
            session = null;
            root.Unregister();
            registered = false;
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync();
                }
                catch (Exception)
                {
                    // Preserve the original test failure while still attempting cleanup.
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
                    // Preserve the original failure while still attempting cleanup.
                }
            }

            if (Directory.Exists(testPath))
            {
                Directory.Delete(testPath, recursive: true);
            }
        }
    }

    private sealed class RecordingDemandProvider : ICloudDemandProvider
    {
        private readonly byte[] _content;
        private readonly ConcurrentDictionary<
            CloudProviderNotificationKind,
            TaskCompletionSource<CloudProviderCompletionNotification>> _notifications = new();

        internal RecordingDemandProvider(byte[] content)
        {
            _content = content;
        }

        internal TaskCompletionSource<CloudProviderDehydrateRequest> DehydrateApproval { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<CloudProviderDeleteRequest> DeleteApproval { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource<CloudProviderRenameRequest> RenameApproval { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<Stream> OpenReadAsync(
            CloudFileFetchRequest request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(new MemoryStream(_content, writable: false));
        }

        public ValueTask<CloudProviderPolicyDecision> ApproveDehydrateAsync(
            CloudProviderDehydrateRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DehydrateApproval.TrySetResult(request);
            return ValueTask.FromResult(CloudProviderPolicyDecision.Allow);
        }

        public ValueTask<CloudProviderPolicyDecision> ApproveDeleteAsync(
            CloudProviderDeleteRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteApproval.TrySetResult(request);
            return ValueTask.FromResult(CloudProviderPolicyDecision.Allow);
        }

        public ValueTask<CloudProviderPolicyDecision> ApproveRenameAsync(
            CloudProviderRenameRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenameApproval.TrySetResult(request);
            return ValueTask.FromResult(CloudProviderPolicyDecision.Allow);
        }

        public ValueTask OnCompletionAsync(
            CloudProviderCompletionNotification notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _notifications.GetOrAdd(
                    notification.Kind,
                    static _ => new TaskCompletionSource<CloudProviderCompletionNotification>(
                        TaskCreationOptions.RunContinuationsAsynchronously))
                .TrySetResult(notification);
            return ValueTask.CompletedTask;
        }

        internal async Task<CloudProviderCompletionNotification> WaitForNotificationAsync(
            CloudProviderNotificationKind kind)
        {
            TaskCompletionSource<CloudProviderCompletionNotification> completion =
                _notifications.GetOrAdd(
                    kind,
                    static _ => new TaskCompletionSource<CloudProviderCompletionNotification>(
                        TaskCreationOptions.RunContinuationsAsynchronously));
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
