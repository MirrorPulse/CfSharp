using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CfSharp.Native.Tests;

public sealed class PlaceholderManagementLayoutTests
{
    [Fact]
    public void FileRangeLayoutMatchesWindowsSdk()
    {
        Assert.Equal(16, Marshal.SizeOf<CfFileRange>());
        Assert.Equal(0, Marshal.OffsetOf<CfFileRange>(nameof(CfFileRange.StartingOffset)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<CfFileRange>(nameof(CfFileRange.Length)).ToInt32());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<CfFileRange>());
    }

    [Fact]
    public void PlaceholderManagementConstantsMatchWindowsSdk()
    {
        Assert.Equal(0x00000001u, (uint)CfConvertFlags.MarkInSync);
        Assert.Equal(0x00000002u, (uint)CfConvertFlags.Dehydrate);
        Assert.Equal(0x00000004u, (uint)CfConvertFlags.EnableOnDemandPopulation);
        Assert.Equal(0x00000008u, (uint)CfConvertFlags.AlwaysFull);
        Assert.Equal(0x00000010u, (uint)CfConvertFlags.ForceConvertToCloudFile);

        Assert.Equal(0x00000001u, (uint)CfUpdateFlags.VerifyInSync);
        Assert.Equal(0x00000002u, (uint)CfUpdateFlags.MarkInSync);
        Assert.Equal(0x00000004u, (uint)CfUpdateFlags.Dehydrate);
        Assert.Equal(0x00000008u, (uint)CfUpdateFlags.EnableOnDemandPopulation);
        Assert.Equal(0x00000010u, (uint)CfUpdateFlags.DisableOnDemandPopulation);
        Assert.Equal(0x00000020u, (uint)CfUpdateFlags.RemoveFileIdentity);
        Assert.Equal(0x00000040u, (uint)CfUpdateFlags.ClearInSync);
        Assert.Equal(0x00000080u, (uint)CfUpdateFlags.RemoveProperty);
        Assert.Equal(0x00000100u, (uint)CfUpdateFlags.PassthroughFsMetadata);
        Assert.Equal(0x00000200u, (uint)CfUpdateFlags.AlwaysFull);
        Assert.Equal(0x00000400u, (uint)CfUpdateFlags.AllowPartial);

        Assert.Equal(0u, (uint)CfRevertFlags.None);
        Assert.Equal(0u, (uint)CfHydrateFlags.None);
        Assert.Equal(0x00000001u, (uint)CfDehydrateFlags.Background);
        Assert.Equal(4, (int)CfPinState.Inherit);
        Assert.Equal(0x00000001u, (uint)CfSetPinFlags.Recurse);
        Assert.Equal(0x00000002u, (uint)CfSetPinFlags.RecurseOnly);
        Assert.Equal(0x00000004u, (uint)CfSetPinFlags.RecurseStopOnError);
        Assert.Equal(1, (int)CfInSyncState.InSync);
        Assert.Equal(0u, (uint)CfSetInSyncFlags.None);
    }
}
