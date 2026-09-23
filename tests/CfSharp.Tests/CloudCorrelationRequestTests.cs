namespace CfSharp.Tests;

public sealed class CloudCorrelationRequestTests
{
    [Fact]
    public void CallbackRequestModelsRetainManagedCorrelationValue()
    {
        CloudCorrelationVector vector = CloudCorrelationVector.Create(1, "request-value");
        CloudFileFetchRequest fetch = new(
            "file.txt",
            [1, 2, 3],
            4096,
            0,
            4096,
            correlationVector: vector);
        CloudProviderFetchPlaceholdersRequest placeholders = new(
            "directory",
            [4, 5],
            "*",
            null,
            vector);
        CloudProviderValidateDataRequest validation = new(
            "file.txt",
            [1],
            4096,
            0,
            4096,
            explicitHydration: true,
            correlationVector: vector);

        Assert.Equal(vector, fetch.CorrelationVector);
        Assert.Equal(vector, placeholders.CorrelationVector);
        Assert.Equal(vector, validation.CorrelationVector);
        Assert.Equal(vector, placeholders.WithContinuationToken("next").CorrelationVector);
    }
}
