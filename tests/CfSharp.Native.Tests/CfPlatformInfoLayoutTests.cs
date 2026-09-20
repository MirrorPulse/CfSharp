using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CfSharp.Native.Tests;

public sealed class CfPlatformInfoLayoutTests
{
    [Fact]
    public void LayoutMatchesWindowsSdk()
    {
        Assert.Equal(12, Marshal.SizeOf<CfPlatformInfo>());
        Assert.Equal(0, Marshal.OffsetOf<CfPlatformInfo>(nameof(CfPlatformInfo.BuildNumber)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<CfPlatformInfo>(nameof(CfPlatformInfo.RevisionNumber)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<CfPlatformInfo>(nameof(CfPlatformInfo.IntegrationNumber)).ToInt32());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<CfPlatformInfo>());
    }
}
