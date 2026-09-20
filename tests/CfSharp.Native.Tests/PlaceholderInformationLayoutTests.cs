using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CfSharp.Native.Tests;

public sealed class PlaceholderInformationLayoutTests
{
    [Fact]
    public void PlaceholderInformationLayoutsMatchWindowsSdk()
    {
        AssertLayout<CfPlaceholderBasicInfo>(32,
            (nameof(CfPlaceholderBasicInfo.PinState), 0),
            (nameof(CfPlaceholderBasicInfo.InSyncState), 4),
            (nameof(CfPlaceholderBasicInfo.FileId), 8),
            (nameof(CfPlaceholderBasicInfo.SyncRootFileId), 16),
            (nameof(CfPlaceholderBasicInfo.FileIdentityLength), 24),
            (nameof(CfPlaceholderBasicInfo.FileIdentity), 28));
        AssertLayout<CfPlaceholderStandardInfo>(64,
            (nameof(CfPlaceholderStandardInfo.OnDiskDataSize), 0),
            (nameof(CfPlaceholderStandardInfo.ValidatedDataSize), 8),
            (nameof(CfPlaceholderStandardInfo.ModifiedDataSize), 16),
            (nameof(CfPlaceholderStandardInfo.PropertiesSize), 24),
            (nameof(CfPlaceholderStandardInfo.PinState), 32),
            (nameof(CfPlaceholderStandardInfo.InSyncState), 36),
            (nameof(CfPlaceholderStandardInfo.FileId), 40),
            (nameof(CfPlaceholderStandardInfo.SyncRootFileId), 48),
            (nameof(CfPlaceholderStandardInfo.FileIdentityLength), 56),
            (nameof(CfPlaceholderStandardInfo.FileIdentity), 60));
    }

    [Fact]
    public void SupportingWin32LayoutsMatchWindowsSdk()
    {
        Assert.Equal(130, Marshal.SizeOf<CfCorrelationVector>());
        Assert.Equal(0, Marshal.OffsetOf<CfCorrelationVector>(nameof(CfCorrelationVector.Version)).ToInt32());
        Assert.Equal(1, Marshal.OffsetOf<CfCorrelationVector>(nameof(CfCorrelationVector.Vector)).ToInt32());
        Assert.Equal(8, Marshal.SizeOf<CfFileTime>());
        AssertLayout<CfWin32FindData>(592,
            (nameof(CfWin32FindData.FileAttributes), 0),
            (nameof(CfWin32FindData.CreationTime), 4),
            (nameof(CfWin32FindData.FileSizeHigh), 28),
            (nameof(CfWin32FindData.Reserved0), 36),
            (nameof(CfWin32FindData.FileName), 44),
            (nameof(CfWin32FindData.AlternateFileName), 564));
    }

    [Fact]
    public void PlaceholderInformationConstantsMatchWindowsSdk()
    {
        Assert.Equal(0, (int)CfPlaceholderInfoClass.Basic);
        Assert.Equal(1, (int)CfPlaceholderInfoClass.Standard);
        Assert.Equal(1, (int)CfPlaceholderRangeInfoClass.OnDisk);
        Assert.Equal(2, (int)CfPlaceholderRangeInfoClass.Validated);
        Assert.Equal(3, (int)CfPlaceholderRangeInfoClass.Modified);
        Assert.Equal(0x00000001u, (uint)CfPlaceholderState.Placeholder);
        Assert.Equal(0x00000002u, (uint)CfPlaceholderState.SyncRoot);
        Assert.Equal(0x00000004u, (uint)CfPlaceholderState.EssentialPropertyPresent);
        Assert.Equal(0x00000008u, (uint)CfPlaceholderState.InSync);
        Assert.Equal(0x00000010u, (uint)CfPlaceholderState.Partial);
        Assert.Equal(0x00000020u, (uint)CfPlaceholderState.PartiallyOnDisk);
        Assert.Equal(uint.MaxValue, (uint)CfPlaceholderState.Invalid);
    }

    private static void AssertLayout<T>(
        int expectedSize,
        params (string FieldName, int ExpectedOffset)[] fields)
        where T : struct
    {
        Assert.Equal(expectedSize, Marshal.SizeOf<T>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<T>());

        foreach ((string fieldName, int expectedOffset) in fields)
        {
            Assert.Equal(expectedOffset, Marshal.OffsetOf<T>(fieldName).ToInt32());
        }
    }
}
