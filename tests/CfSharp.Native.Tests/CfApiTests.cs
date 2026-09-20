namespace CfSharp.Native.Tests;

public sealed class CfApiTests
{
    [Fact]
    public void GetPlatformInfoReturnsInstalledPlatformVersion()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        int result = CfApi.CfGetPlatformInfo(out CfPlatformInfo platformInfo);

        Assert.Equal(0, result);
        Assert.NotEqual(0u, platformInfo.BuildNumber);
        Assert.NotEqual(0u, platformInfo.IntegrationNumber);
    }
}
