using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CfSharp.Native.Tests;

public sealed class PlaceholderTransferLayoutTests
{
    [Fact]
    public void PlaceholderLayoutsMatchWindowsSdkForCurrentArchitecture()
    {
        AssertLayout<CfFileBasicInfo>(40,
            (nameof(CfFileBasicInfo.CreationTime), 0),
            (nameof(CfFileBasicInfo.LastAccessTime), 8),
            (nameof(CfFileBasicInfo.LastWriteTime), 16),
            (nameof(CfFileBasicInfo.ChangeTime), 24),
            (nameof(CfFileBasicInfo.FileAttributes), 32));
        AssertLayout<CfFsMetadata>(48,
            (nameof(CfFsMetadata.BasicInfo), 0),
            (nameof(CfFsMetadata.FileSize), 40));

        int expectedSize = IntPtr.Size == 8 ? 88 : 80;
        int[] expectedOffsets = IntPtr.Size == 8
            ? [0, 8, 56, 64, 68, 72, 80]
            : [0, 8, 56, 60, 64, 68, 72];
        AssertLayout<CfPlaceholderCreateInfo>(expectedSize,
            (nameof(CfPlaceholderCreateInfo.RelativeFileName), expectedOffsets[0]),
            (nameof(CfPlaceholderCreateInfo.FsMetadata), expectedOffsets[1]),
            (nameof(CfPlaceholderCreateInfo.FileIdentity), expectedOffsets[2]),
            (nameof(CfPlaceholderCreateInfo.FileIdentityLength), expectedOffsets[3]),
            (nameof(CfPlaceholderCreateInfo.Flags), expectedOffsets[4]),
            (nameof(CfPlaceholderCreateInfo.Result), expectedOffsets[5]),
            (nameof(CfPlaceholderCreateInfo.CreateUsn), expectedOffsets[6]));
    }

    [Fact]
    public void OperationLayoutsMatchWindowsSdkForCurrentArchitecture()
    {
        AssertLayout<CfTransferKey>(8, (nameof(CfTransferKey.Internal), 0));
        AssertLayout<CfRequestKey>(8, (nameof(CfRequestKey.Internal), 0));
        Assert.Equal(4, Marshal.SizeOf<NtStatus>());
        AssertLayout<CfSyncStatus>(24,
            (nameof(CfSyncStatus.StructSize), 0),
            (nameof(CfSyncStatus.Code), 4),
            (nameof(CfSyncStatus.DescriptionOffset), 8),
            (nameof(CfSyncStatus.DeviceIdOffset), 16));

        int operationInfoSize = IntPtr.Size == 8 ? 48 : 40;
        int[] operationInfoOffsets = IntPtr.Size == 8
            ? [0, 4, 8, 16, 24, 32, 40]
            : [0, 4, 8, 16, 24, 28, 32];
        AssertLayout<CfOperationInfo>(operationInfoSize,
            (nameof(CfOperationInfo.StructSize), operationInfoOffsets[0]),
            (nameof(CfOperationInfo.Type), operationInfoOffsets[1]),
            (nameof(CfOperationInfo.ConnectionKey), operationInfoOffsets[2]),
            (nameof(CfOperationInfo.TransferKey), operationInfoOffsets[3]),
            (nameof(CfOperationInfo.CorrelationVector), operationInfoOffsets[4]),
            (nameof(CfOperationInfo.SyncStatus), operationInfoOffsets[5]),
            (nameof(CfOperationInfo.RequestKey), operationInfoOffsets[6]));

        AssertLayout<CfOperationParameters>(IntPtr.Size == 8 ? 48 : 40,
            (nameof(CfOperationParameters.ParamSize), 0),
            (nameof(CfOperationParameters.TransferData), 8),
            (nameof(CfOperationParameters.RetrieveData), 8),
            (nameof(CfOperationParameters.AckDelete), 8));
        AssertLayout<CfOperationTransferDataParameters>(32,
            (nameof(CfOperationTransferDataParameters.Flags), 0),
            (nameof(CfOperationTransferDataParameters.CompletionStatus), 4),
            (nameof(CfOperationTransferDataParameters.Buffer), 8),
            (nameof(CfOperationTransferDataParameters.Offset), 16),
            (nameof(CfOperationTransferDataParameters.Length), 24));
        Assert.Equal(IntPtr.Size == 8 ? 40 : 32, Marshal.SizeOf<CfOperationRetrieveDataParameters>());
        Assert.Equal(32, Marshal.SizeOf<CfOperationTransferPlaceholdersParameters>());
    }

    [Fact]
    public void ConstantsAndStatusesMatchWindowsSdk()
    {
        Assert.Equal(0x00000008u, (uint)CfPlaceholderCreateFlags.AlwaysFull);
        Assert.Equal(0x00000001u, (uint)CfCreateFlags.StopOnError);
        Assert.Equal(7, (int)CfOperationType.AckRename);
        Assert.Equal(0x00000002u, (uint)CfOperationTransferPlaceholdersFlags.DisableOnDemandPopulation);
        Assert.Equal(0, NtStatus.Success.Value);
        Assert.Equal(unchecked((int)0xC000CF12), NtStatus.CloudFileUnsuccessful.Value);
        Assert.Equal(unchecked((int)0xC000CF16), NtStatus.CloudFileRequestAborted.Value);
        Assert.Equal("0xC000CF1B", NtStatus.CloudFileRequestCanceled.ToString());
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
