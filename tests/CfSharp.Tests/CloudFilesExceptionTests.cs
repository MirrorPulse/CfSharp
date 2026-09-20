namespace CfSharp.Tests;

public sealed class CloudFilesExceptionTests
{
    [Fact]
    public void FromHResultPreservesWin32FailureDetails()
    {
        const int accessDeniedHResult = unchecked((int)0x80070005);

        CloudFilesException exception =
            CloudFilesException.FromHResult("TestOperation", accessDeniedHResult);

        Assert.Equal("TestOperation", exception.Operation);
        Assert.Equal(accessDeniedHResult, exception.HResult);
        Assert.Equal(5, exception.Win32ErrorCode);
        Assert.NotNull(exception.InnerException);
        Assert.Contains("0x80070005", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromHResultDoesNotInventWin32CodeForOtherFacilities()
    {
        const int genericFailureHResult = unchecked((int)0x80004005);

        CloudFilesException exception =
            CloudFilesException.FromHResult("TestOperation", genericFailureHResult);

        Assert.Equal(genericFailureHResult, exception.HResult);
        Assert.Null(exception.Win32ErrorCode);
    }

    [Fact]
    public void FromHResultPreservesAssociatedPath()
    {
        const int accessDeniedHResult = unchecked((int)0x80070005);
        const string path = @"C:\Cloud";

        CloudFilesException exception =
            CloudFilesException.FromHResult("CloudSyncRoot.Register", path, accessDeniedHResult);

        Assert.Equal(path, exception.Path);
        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }
}
