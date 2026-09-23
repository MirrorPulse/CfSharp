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

    [Fact]
    public void PlaceholderTransferResultsPreservePartialEntryOutcomes()
    {
        CloudTransferPlaceholderBatchResult result = new(
        [
            new CloudTransferPlaceholderEntryResult(0, IsProcessed: true, NativeResult: 0),
            new CloudTransferPlaceholderEntryResult(1, IsProcessed: true, NativeResult: -1),
            new CloudTransferPlaceholderEntryResult(2, IsProcessed: false, NativeResult: null),
        ]);

        Assert.Equal(2, result.EntriesProcessed);
        Assert.False(result.IsComplete);
        Assert.False(result.IsSuccessful);
        Assert.True(result.Entries[0].IsSuccessful);
        Assert.False(result.Entries[1].IsSuccessful);
        Assert.False(result.Entries[2].IsProcessed);
    }
}
