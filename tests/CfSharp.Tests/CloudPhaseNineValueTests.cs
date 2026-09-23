namespace CfSharp.Tests;

public sealed class CloudPhaseNineValueTests
{
    [Fact]
    public void CorrelationVectorValidatesNativeBoundsAndRoundTripsText()
    {
        CloudCorrelationVector vector = CloudCorrelationVector.Create(2, "abc/123");

        Assert.Equal((byte)2, vector.Version);
        Assert.Equal("abc/123", vector.Value);
        Assert.Equal("2;abc/123", vector.ToString());
        Assert.True(CloudCorrelationVector.TryParse("2;abc/123", out CloudCorrelationVector parsed));
        Assert.Equal(vector, parsed);
        Assert.False(CloudCorrelationVector.TryParse("2;\0bad", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => CloudCorrelationVector.Create(3, "value"));
        Assert.Throws<ArgumentException>(() => CloudCorrelationVector.Create(1, new string('x', 65)));
    }

    [Fact]
    public void LeaseOptionsRejectUnsupportedOrConflictingFlags()
    {
        CloudItemLeaseOptions.ReadOnly.Validate();
        CloudItemLeaseOptions.ExclusiveWrite.Validate();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CloudItemLeaseOptions { Access = (CloudItemLeaseAccess)8 }.Validate());
        Assert.Throws<ArgumentException>(() =>
            new CloudItemLeaseOptions
            {
                Access = CloudItemLeaseAccess.Read,
                Exclusive = true,
                Foreground = true,
            }.Validate());
    }

    [Fact]
    public void ProgressTargetRequiresAnOpaqueRequestIdentifier()
    {
        Assert.Throws<ArgumentException>(() => CloudProgressTarget.Request(default));

        CloudProgressTarget target = CloudProgressTarget.Request(
            new CloudProviderRequestId(17),
            targetSessionId: 4);

        Assert.NotNull(target.RequestId);
        Assert.Equal((uint)4, target.TargetSessionId);
        Assert.Equal("request:0000000000000011", target.RequestId!.Value.ToString());
    }

    [Fact]
    public void ProgressReporterReturnsThrottledResultWithoutChangingLegacySink()
    {
        List<(long Completed, long Total)> reports = [];
        CloudProviderProgressReporter reporter = new(
            (completed, total) => reports.Add((completed, total)));

        CloudProgressReportResult first = reporter.Report(1, 10);
        CloudProgressReportResult regression = reporter.Report(0, 10);
        CloudProgressReportResult terminal = reporter.Report(10, 10);

        Assert.Equal(CloudProgressReportState.Reported, first.State);
        Assert.Equal(CloudProgressReportState.Throttled, regression.State);
        Assert.Equal(CloudProgressReportState.Reported, terminal.State);
        Assert.Equal([(1L, 10L), (10L, 10L)], reports);
    }

}
