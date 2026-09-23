using System.Diagnostics;

namespace CfSharp.Tests;

public sealed class CloudDiagnosticsTests
{
    [Fact]
    public async Task ThrowingActivityListenerCannotBreakProviderWork()
    {
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == CloudDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _ => throw new InvalidOperationException("diagnostic listener failure"),
        };
        ActivitySource.AddActivityListener(listener);

        bool completed = false;
        CloudProviderWorkItem work = new(
            CloudProviderRequestKind.FetchData,
            new CancellationTokenSource(),
            _ =>
            {
                completed = true;
                return ValueTask.CompletedTask;
            },
            () => { },
            exception => throw exception);

        await work.RunAsync();

        Assert.True(completed);
    }

    [Fact]
    public void DiagnosticsNamesAreStableAndDoNotContainApplicationData()
    {
        Assert.Equal("CfSharp", CloudDiagnostics.ActivitySourceName);
        Assert.Equal("CfSharp", CloudDiagnostics.MeterName);
        Assert.DoesNotContain("path", CloudDiagnostics.ActivitySourceName, StringComparison.OrdinalIgnoreCase);
    }
}
