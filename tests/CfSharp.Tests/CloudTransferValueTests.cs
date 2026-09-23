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

    [Fact]
    public void TransferValidationExceptionPreservesOperationAndPath()
    {
        IOException inner = new("metadata unavailable");
        CloudTransferValidationException exception = new(
            "CloudTransfer.RangeValidation",
            "C:\\sync\\file.bin",
            "The item length could not be read.",
            inner);

        Assert.Equal("CloudTransfer.RangeValidation", exception.Operation);
        Assert.Equal("C:\\sync\\file.bin", exception.Path);
        Assert.Same(inner, exception.InnerException);
    }
}
