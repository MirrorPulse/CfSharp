using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CfSharp.Native.Tests;

public sealed class CallbackLayoutTests
{
    [Fact]
    public void ConnectionAndCallbackInfoLayoutsMatchWindowsSdk()
    {
        AssertLayout<CfConnectionKey>(8,
            (nameof(CfConnectionKey.Internal), 0));
        AssertLayout<CfTransferKey>(8,
            (nameof(CfTransferKey.Internal), 0));
        AssertLayout<CfRequestKey>(8,
            (nameof(CfRequestKey.Internal), 0));

        if (IntPtr.Size == 8)
        {
            AssertLayout<CfProcessInfo>(48,
                (nameof(CfProcessInfo.StructSize), 0),
                (nameof(CfProcessInfo.ProcessId), 4),
                (nameof(CfProcessInfo.ImagePath), 8),
                (nameof(CfProcessInfo.SessionId), 40));
            AssertLayout<CfCallbackRegistration>(16,
                (nameof(CfCallbackRegistration.Type), 0),
                (nameof(CfCallbackRegistration.Callback), 8));
            AssertLayout<CfCallbackInfo>(152,
                (nameof(CfCallbackInfo.StructSize), 0),
                (nameof(CfCallbackInfo.ConnectionKey), 8),
                (nameof(CfCallbackInfo.CallbackContext), 16),
                (nameof(CfCallbackInfo.VolumeSerialNumber), 40),
                (nameof(CfCallbackInfo.SyncRootFileId), 48),
                (nameof(CfCallbackInfo.SyncRootIdentity), 56),
                (nameof(CfCallbackInfo.SyncRootIdentityLength), 64),
                (nameof(CfCallbackInfo.FileId), 72),
                (nameof(CfCallbackInfo.FileSize), 80),
                (nameof(CfCallbackInfo.FileIdentity), 88),
                (nameof(CfCallbackInfo.FileIdentityLength), 96),
                (nameof(CfCallbackInfo.NormalizedPath), 104),
                (nameof(CfCallbackInfo.TransferKey), 112),
                (nameof(CfCallbackInfo.PriorityHint), 120),
                (nameof(CfCallbackInfo.CorrelationVector), 128),
                (nameof(CfCallbackInfo.ProcessInfo), 136),
                (nameof(CfCallbackInfo.RequestKey), 144));
        }
        else
        {
            AssertLayout<CfProcessInfo>(28,
                (nameof(CfProcessInfo.StructSize), 0),
                (nameof(CfProcessInfo.ProcessId), 4),
                (nameof(CfProcessInfo.ImagePath), 8),
                (nameof(CfProcessInfo.SessionId), 24));
            AssertLayout<CfCallbackRegistration>(8,
                (nameof(CfCallbackRegistration.Type), 0),
                (nameof(CfCallbackRegistration.Callback), 4));
            AssertLayout<CfCallbackInfo>(112,
                (nameof(CfCallbackInfo.StructSize), 0),
                (nameof(CfCallbackInfo.ConnectionKey), 8),
                (nameof(CfCallbackInfo.CallbackContext), 16),
                (nameof(CfCallbackInfo.VolumeSerialNumber), 28),
                (nameof(CfCallbackInfo.SyncRootFileId), 32),
                (nameof(CfCallbackInfo.SyncRootIdentity), 40),
                (nameof(CfCallbackInfo.SyncRootIdentityLength), 44),
                (nameof(CfCallbackInfo.FileId), 48),
                (nameof(CfCallbackInfo.FileSize), 56),
                (nameof(CfCallbackInfo.FileIdentity), 64),
                (nameof(CfCallbackInfo.FileIdentityLength), 68),
                (nameof(CfCallbackInfo.NormalizedPath), 72),
                (nameof(CfCallbackInfo.TransferKey), 80),
                (nameof(CfCallbackInfo.PriorityHint), 88),
                (nameof(CfCallbackInfo.CorrelationVector), 92),
                (nameof(CfCallbackInfo.ProcessInfo), 96),
                (nameof(CfCallbackInfo.RequestKey), 104));
        }
    }

    [Fact]
    public void CallbackParameterLayoutsMatchWindowsSdk()
    {
        AssertLayout<CfCallbackParameters>(64,
            (nameof(CfCallbackParameters.ParamSize), 0),
            (nameof(CfCallbackParameters.Cancel), 8),
            (nameof(CfCallbackParameters.FetchData), 8),
            (nameof(CfCallbackParameters.RenameCompletion), 8));
        AssertLayout<CfCallbackCancelParameters>(24,
            (nameof(CfCallbackCancelParameters.Flags), 0),
            (nameof(CfCallbackCancelParameters.FileOffset), 8),
            (nameof(CfCallbackCancelParameters.Length), 16));
        AssertLayout<CfCallbackFetchDataParameters>(56,
            (nameof(CfCallbackFetchDataParameters.Flags), 0),
            (nameof(CfCallbackFetchDataParameters.RequiredFileOffset), 8),
            (nameof(CfCallbackFetchDataParameters.RequiredLength), 16),
            (nameof(CfCallbackFetchDataParameters.OptionalFileOffset), 24),
            (nameof(CfCallbackFetchDataParameters.OptionalLength), 32),
            (nameof(CfCallbackFetchDataParameters.LastDehydrationTime), 40),
            (nameof(CfCallbackFetchDataParameters.LastDehydrationReason), 48));
        AssertLayout<CfCallbackValidateDataParameters>(24,
            (nameof(CfCallbackValidateDataParameters.Flags), 0),
            (nameof(CfCallbackValidateDataParameters.RequiredFileOffset), 8),
            (nameof(CfCallbackValidateDataParameters.RequiredLength), 16));
        Assert.Equal(
            IntPtr.Size == 8 ? 16 : 8,
            Marshal.SizeOf<CfCallbackFetchPlaceholdersParameters>());
        Assert.Equal(IntPtr.Size == 8 ? 16 : 8, Marshal.SizeOf<CfCallbackRenameParameters>());
    }

    [Fact]
    public unsafe void CallbackConstantsMatchWindowsSdk()
    {
        CfCallbackRegistration terminator = CfCallbackRegistration.End;

        Assert.Equal(-1, (int)CfCallbackType.None);
        Assert.Equal(12, (int)CfCallbackType.NotifyRenameCompletion);
        Assert.Equal(CfCallbackType.None, terminator.Type);
        Assert.True(terminator.Callback is null);
        Assert.Equal(0x00000008u, (uint)CfConnectFlags.BlockSelfImplicitHydration);
        Assert.Equal(0x00000002u, (uint)CfCallbackFetchDataFlags.ExplicitHydration);
        Assert.Equal(0x00000002u, (uint)CfCallbackCancelFlags.IoAborted);
        Assert.Equal(15, CfApi.MaxPriorityHint);
        Assert.Equal(-1, CfApi.EndOfFile);
        Assert.Equal(0, CfApi.DefaultRequestKey);
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
