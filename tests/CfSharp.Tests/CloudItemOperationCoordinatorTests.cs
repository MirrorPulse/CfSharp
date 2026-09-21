namespace CfSharp.Tests;

public sealed class CloudItemOperationCoordinatorTests
{
    [Fact]
    public async Task OverlappingScopesSerializeWhileNonOverlappingScopesProceed()
    {
        string root = Path.Combine(Path.GetTempPath(), "CfSharp-operation-tests", Guid.NewGuid().ToString("N"));
        string subtreePath = Path.Combine(root, "folder");
        string childPath = Path.Combine(subtreePath, "child.bin");
        string siblingPrefixPath = Path.Combine(root, "folder-other");
        CloudItemOperationCoordinator coordinator = new();

        using CloudItemOperationCoordinator.CloudItemOperationPathLease subtree =
            await coordinator.AcquireAsync([CloudItemOperationScope.Subtree(subtreePath)]);
        Task<CloudItemOperationCoordinator.CloudItemOperationPathLease> blockedChild = coordinator
            .AcquireAsync([CloudItemOperationScope.Exact(childPath)])
            .AsTask();

        using CloudItemOperationCoordinator.CloudItemOperationPathLease nonOverlapping =
            await coordinator.AcquireAsync([CloudItemOperationScope.Exact(siblingPrefixPath)]);
        Assert.False(blockedChild.IsCompleted);

        subtree.Dispose();
        using CloudItemOperationCoordinator.CloudItemOperationPathLease child =
            await blockedChild.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CanceledWaiterDoesNotRetainAPathScope()
    {
        string root = Path.Combine(Path.GetTempPath(), "CfSharp-operation-tests", Guid.NewGuid().ToString("N"));
        string child = Path.Combine(root, "child.bin");
        CloudItemOperationCoordinator coordinator = new();
        using CloudItemOperationCoordinator.CloudItemOperationPathLease subtree =
            await coordinator.AcquireAsync([CloudItemOperationScope.Subtree(root)]);
        using CancellationTokenSource cancellation = new();

        Task canceled = coordinator
            .AcquireAsync([CloudItemOperationScope.Exact(child)], cancellation.Token)
            .AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        subtree.Dispose();
        using CloudItemOperationCoordinator.CloudItemOperationPathLease retry =
            await coordinator.AcquireAsync([CloudItemOperationScope.Exact(child)])
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ScopeValidationRejectsRelativeAndEmptySets()
    {
        Assert.Throws<ArgumentException>(() => CloudItemOperationScope.Exact("relative.bin"));

        CloudItemOperationCoordinator coordinator = new();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await coordinator.AcquireAsync([]));
    }
}
