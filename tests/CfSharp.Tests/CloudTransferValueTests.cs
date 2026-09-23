namespace CfSharp.Tests;

public sealed class CloudTransferValueTests
{
    [Fact]
    public void TransferOptionsValidateBatchBoundsAndStatusText()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CloudTransferOptions { MaxBatchEntries = 0 }.Validate());
        Assert.Throws<ArgumentException>(() =>
            new CloudTransferOptions
            {
                OperationStatus = new CloudSyncStatus { Description = "bad\0status" },
            }.Validate());
    }
}
