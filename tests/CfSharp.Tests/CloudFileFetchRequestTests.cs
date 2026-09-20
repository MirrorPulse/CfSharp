namespace CfSharp.Tests;

public sealed class CloudFileFetchRequestTests
{
    [Fact]
    public void ConstructorCreatesImmutableIdentitySnapshot()
    {
        byte[] identity = [1, 2, 3];
        CloudFileFetchRequest request = new("file.bin", identity, 100, 10, 20);

        identity[0] = 99;

        Assert.True(request.FileIdentity.SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.Equal("file.bin", request.NormalizedPath);
        Assert.Equal(100, request.FileSize);
        Assert.Equal(10, request.Offset);
        Assert.Equal(20, request.Length);
    }
}
