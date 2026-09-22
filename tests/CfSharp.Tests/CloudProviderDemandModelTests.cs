namespace CfSharp.Tests;

public sealed class CloudProviderDemandModelTests
{
    [Fact]
    public void SessionOptionsRejectUnboundedOrMisalignedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CloudProviderSessionOptions { QueueCapacity = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CloudProviderSessionOptions { TransferChunkSize = 5000 }.Validate());
    }

    [Fact]
    public void DirectoryPageCopiesAndValidatesChildren()
    {
        CloudFilePlaceholderSpec child = CloudFilePlaceholderSpec.CreateBuilder(
            "child.txt",
            "remote-child",
            3).Build();
        List<CloudPlaceholderSpec> children = [child];
        CloudProviderDirectoryPage page = new(children, "next", 2);

        children.Clear();

        Assert.Single(page.Children);
        Assert.Equal("next", page.ContinuationToken);
        Assert.False(page.IsComplete);
    }

    [Fact]
    public void ValidationResultFactoriesPreserveOutcome()
    {
        Assert.Equal(
            CloudProviderValidationStatus.Accepted,
            CloudProviderValidationResult.Accepted().Status);
        Assert.Equal(
            CloudProviderValidationStatus.Rejected,
            CloudProviderValidationResult.Rejected().Status);
        Assert.Equal(
            CloudProviderValidationStatus.Changed,
            CloudProviderValidationResult.Changed().Status);
    }
}
