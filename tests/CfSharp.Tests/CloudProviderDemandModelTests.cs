using System.Runtime.Versioning;

namespace CfSharp.Tests;

[SupportedOSPlatform("windows10.0.16299")]
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
    public void FetchDataBufferSizeClampsBeforeConvertingLargeRequestLength()
    {
        Assert.Equal(
            64 * 1024,
            CloudProviderSession.GetTransferBufferSize(64 * 1024, (long)int.MaxValue + 1));
        Assert.Equal(
            4096,
            CloudProviderSession.GetTransferBufferSize(64 * 1024, 4096));
    }

    [Fact]
    public void CanceledDirectoryContinuationCanBeDiscarded()
    {
        System.Collections.Concurrent.ConcurrentDictionary<string, string> continuations =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["\\directory"] = "next",
            };

        Assert.True(CloudProviderSession.TryRemoveDirectoryContinuation(continuations, "\\directory"));
        Assert.False(continuations.ContainsKey("\\directory"));
        Assert.False(CloudProviderSession.TryRemoveDirectoryContinuation(continuations, "\\directory"));
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

    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public void PlaceholderTransferValidationRequiresEveryEntryToSucceed()
    {
        CloudProviderSession.ValidatePlaceholderTransferResults([0, 0], 2, "root");

        Assert.Throws<InvalidDataException>(() =>
            CloudProviderSession.ValidatePlaceholderTransferResults([0, 0], 1, "root"));

        CloudFilesException failure = Assert.Throws<CloudFilesException>(() =>
            CloudProviderSession.ValidatePlaceholderTransferResults(
                [0, unchecked((int)0x80070005)],
                2,
                "root"));
        Assert.Equal("CloudProviderSession.TransferPlaceholders.Entry", failure.Operation);
        Assert.Equal("root", failure.Path);
        Assert.Equal(unchecked((int)0x80070005), failure.HResult);
    }

    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public void PlaceholderTransferValidationRejectsImpossibleProcessedCount()
    {
        Assert.Throws<InvalidDataException>(() =>
            CloudProviderSession.ValidatePlaceholderTransferResults([0], 2, "root"));
    }
}
