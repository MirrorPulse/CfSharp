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
    public void DirectoryPageRejectsDuplicateNamesAndIdentities()
    {
        CloudFilePlaceholderSpec first = CloudFilePlaceholderSpec.CreateBuilder(
            "first.txt",
            new CloudPlaceholderIdentity(Guid.NewGuid(), "remote-first"),
            3).Build();
        CloudFilePlaceholderSpec duplicateName = CloudFilePlaceholderSpec.CreateBuilder(
            "FIRST.TXT",
            new CloudPlaceholderIdentity(Guid.NewGuid(), "remote-second"),
            3).Build();
        Assert.Throws<ArgumentException>(() =>
            new CloudProviderDirectoryPage([first, duplicateName]));

        CloudFilePlaceholderSpec duplicateIdentity = CloudFilePlaceholderSpec.CreateBuilder(
            "second.txt",
            first.Identity,
            3).Build();
        Assert.Throws<ArgumentException>(() =>
            new CloudProviderDirectoryPage([first, duplicateIdentity]));
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

    [Fact]
    public void ProgressReporterDropsRegressionsAndAlwaysForwardsTerminalProgress()
    {
        List<(long Completed, long Total)> reports = [];
        CloudProviderProgressReporter reporter = new(
            (completed, total) => reports.Add((completed, total)));

        reporter.Report(1, 10);
        reporter.Report(0, 10);
        reporter.Report(10, 10);

        Assert.Equal([(1L, 10L), (10L, 10L)], reports);
        Assert.Throws<ArgumentOutOfRangeException>(() => reporter.Report(11, 10));
    }
}
