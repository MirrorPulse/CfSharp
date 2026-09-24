using System.Runtime.Versioning;

namespace CfSharp.Native.Tests;

[SupportedOSPlatform("windows10.0.16299")]
public sealed class CfApiTests
{
    [Fact]
    public unsafe void FixedNativeStringRequiresAZeroTerminatorWithinCapacity()
    {
        Span<char> value = stackalloc char[8];
        value.Fill('x');
        value[0] = 'C';
        value[1] = 'f';
        value[2] = 'S';
        value[3] = 'h';
        value[4] = '\0';

        fixed (char* pointer = value)
        {
            Assert.Equal("CfSh", NativeSyncRoot.ReadFixedString(pointer, value.Length));
            value[4] = 'x';
            try
            {
                _ = NativeSyncRoot.ReadFixedString(pointer, value.Length);
                Assert.Fail("Expected an unterminated native string to be rejected.");
            }
            catch (InvalidDataException)
            {
                // Expected: fixed native strings must terminate inside their ABI field.
            }
        }
    }

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
