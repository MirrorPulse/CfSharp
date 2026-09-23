namespace CfSharp.Tests;

public sealed class CloudProgressV2Tests
{
    [Fact]
    public void ReporterForwardsExplicitTargetAndPreservesNativeResult()
    {
        CloudProgressTarget? observedTarget = null;
        CloudProviderProgressReporter reporter = new(
            (target, _, _) =>
            {
                observedTarget = target;
                return new CloudProgressReportResult(
                    CloudProgressReportState.Reported,
                    UsedV2: true,
                    HResult: 0);
            });
        CloudProgressTarget target = CloudProgressTarget.Request(new CloudProviderRequestId(9), 3);

        CloudProgressReportResult result = reporter.Report(target, 4, 8);

        Assert.Equal(CloudProgressReportState.Reported, result.State);
        Assert.True(result.UsedV2);
        Assert.Same(target, observedTarget);
    }

    [Fact]
    public void SessionOptionsRejectUnknownProgressFallbackPolicy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CloudProviderSessionOptions
            {
                ProgressFallbackPolicy = (CloudProgressFallbackPolicy)99,
            }.Validate());
    }

    [Fact]
    public void ReporterScopesMonotonicStatePerTarget()
    {
        List<CloudProgressTarget> observedTargets = [];
        CloudProviderProgressReporter reporter = new(
            (target, _, _) =>
            {
                observedTargets.Add(target);
                return new CloudProgressReportResult(
                    CloudProgressReportState.Reported,
                    UsedV2: target.RequestId is not null,
                    HResult: 0);
            });
        CloudProgressTarget first = CloudProgressTarget.Request(new CloudProviderRequestId(10), 1);
        CloudProgressTarget second = CloudProgressTarget.Request(new CloudProviderRequestId(11), 1);

        Assert.Equal(CloudProgressReportState.Reported, reporter.Report(first, 8, 10).State);
        Assert.Equal(CloudProgressReportState.Reported, reporter.Report(second, 1, 10).State);
        Assert.Equal([first, second], observedTargets);
    }
}
