namespace CfSharp.Tests;

public sealed class CloudFileFetchRequestTests
{
    [Fact]
    public void ConstructorCreatesImmutableIdentitySnapshot()
    {
        byte[] identity = [1, 2, 3];
        CloudFileFetchRequest request = new("file.bin", identity, 100, 10, 20);

        identity[0] = 99;

        Assert.True(request.FileIdentity.SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.Equal("file.bin", request.NormalizedPath);
        Assert.Equal(100, request.FileSize);
        Assert.Equal(10, request.Offset);
        Assert.Equal(20, request.Length);
    }

    [Fact]
    public async Task RestartHydrationRequiresAnActiveProviderRequest()
    {
        CloudFileFetchRequest request = new("file.bin", [], 100, 0, 100);
        CloudFilePlaceholderSpec replacement = CloudFilePlaceholderSpec.CreateBuilder(
            "file.bin",
            "replacement",
            100).Build();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => request.RestartHydrationAsync(replacement).AsTask());
    }

    [Fact]
    public async Task RestartHydrationDelegatesReplacementAndInSyncChoice()
    {
        CloudPlaceholderSpec? capturedReplacement = null;
        bool? capturedMarkInSync = null;
        CloudFileFetchRequest request = new(
            "file.bin",
            [],
            100,
            0,
            100,
            restartHydration: (replacement, markInSync) =>
            {
                capturedReplacement = replacement;
                capturedMarkInSync = markInSync;
                return ValueTask.CompletedTask;
            });
        CloudFilePlaceholderSpec replacement = CloudFilePlaceholderSpec.CreateBuilder(
            "file.bin",
            "replacement",
            100).Build();

        await request.RestartHydrationAsync(replacement, markInSync: false);

        Assert.Same(replacement, capturedReplacement);
        Assert.False(capturedMarkInSync);
    }
}
