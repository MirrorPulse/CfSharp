namespace CfSharp.IntegrationTests;

public sealed class PlatformSmokeTests
{
    [Fact]
    public void IntegrationTestsRunOnWindows()
    {
        Assert.True(OperatingSystem.IsWindows());
    }
}
