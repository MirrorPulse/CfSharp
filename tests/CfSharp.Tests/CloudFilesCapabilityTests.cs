namespace CfSharp.Tests;

public sealed class CloudFilesCapabilityTests
{
    [Fact]
    public void CapabilityThresholdsMatchPinnedCoverageContract()
    {
        Assert.Equal(16299u, CloudFilesPlatformInfo.MinimumCoreWindowsBuild);
        Assert.Equal(17134u, CloudFilesPlatformInfo.RichStatusMinimumWindowsBuild);
        Assert.Equal(17763u, CloudFilesPlatformInfo.ProviderProgressV2MinimumWindowsBuild);
        Assert.Equal(784u, CloudFilesPlatformInfo.PlaceholderManagementPolicyMinimumIntegration);
        Assert.Equal(1280u, CloudFilesPlatformInfo.FullRestartHydrationMinimumIntegration);
        Assert.Equal(1280u, CloudFilesPlatformInfo.ForceConvertToCloudFileMinimumIntegration);
        Assert.Equal(1536u, CloudFilesPlatformInfo.PlaceholderRangeInfoForHydrationMinimumIntegration);
    }

    [Theory]
    [InlineData(17134u, 783u, CloudFilesCapability.RichSyncRootStatus, true)]
    [InlineData(17133u, 9999u, CloudFilesCapability.RichSyncRootStatus, false)]
    [InlineData(17763u, 0u, CloudFilesCapability.ProviderProgressV2, true)]
    [InlineData(17762u, 9999u, CloudFilesCapability.ProviderProgressV2, false)]
    [InlineData(16299u, 784u, CloudFilesCapability.PlaceholderManagementPolicy, true)]
    [InlineData(16299u, 783u, CloudFilesCapability.PlaceholderManagementPolicy, false)]
    [InlineData(17134u, 1536u, CloudFilesCapability.PlaceholderRangeInfoForHydration, true)]
    [InlineData(17133u, 1536u, CloudFilesCapability.PlaceholderRangeInfoForHydration, false)]
    [InlineData(17134u, 1535u, CloudFilesCapability.PlaceholderRangeInfoForHydration, false)]
    public void CapabilityChecksRequireTheDeclaredBuildAndIntegrationGates(
        uint build,
        uint integration,
        CloudFilesCapability capability,
        bool expected)
    {
        CloudFilesPlatformInfo platform = new(build, 0, integration);

        Assert.Equal(expected, platform.Supports(capability));
    }

    [Fact]
    public void UnknownCapabilityValueFailsClosed()
    {
        CloudFilesPlatformInfo platform = new(26100, 0, 2000);

        Assert.Throws<ArgumentOutOfRangeException>(() => platform.Supports((CloudFilesCapability)999));
    }
}
