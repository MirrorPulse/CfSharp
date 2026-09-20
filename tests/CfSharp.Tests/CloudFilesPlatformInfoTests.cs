namespace CfSharp.Tests;

public sealed class CloudFilesPlatformInfoTests
{
    [Fact]
    public void ConstructorCreatesImmutableSnapshot()
    {
        CloudFilesPlatformInfo platformInfo = new(26100, 9539, 12);

        Assert.Equal(26100u, platformInfo.BuildNumber);
        Assert.Equal(9539u, platformInfo.RevisionNumber);
        Assert.Equal(12u, platformInfo.IntegrationNumber);
    }

    [Theory]
    [InlineData(0u, true)]
    [InlineData(12u, true)]
    [InlineData(13u, false)]
    public void SupportsIntegrationComparesCapabilityLevel(uint minimum, bool expected)
    {
        CloudFilesPlatformInfo platformInfo = new(26100, 9539, 12);

        Assert.Equal(expected, platformInfo.SupportsIntegration(minimum));
    }
}
