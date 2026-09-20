namespace CfSharp.IntegrationTests;

public sealed class CloudFilesPlatformTests
{
    [Fact]
    public void GetCurrentReturnsInstalledPlatformInformation()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        CloudFilesPlatformInfo platformInfo = CloudFilesPlatform.GetCurrent();

        Assert.NotEqual(0u, platformInfo.BuildNumber);
        Assert.NotEqual(0u, platformInfo.IntegrationNumber);
        Assert.True(platformInfo.SupportsIntegration(platformInfo.IntegrationNumber));
    }
}
