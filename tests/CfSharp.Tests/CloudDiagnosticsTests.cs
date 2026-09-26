using System.Collections.Concurrent;
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

    [Fact]
    public void ProviderFailureDiagnosticsCaptureOpaqueKeysAndErrorDetails()
    {
        ConcurrentQueue<Activity> stopped = new();
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == CloudDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        CloudDiagnostics.RecordProviderFailure(
            "CloudProviderSession.FetchData",
            new IOException("injected"),
            connectionKey: 1,
            transferKey: 2,
            requestKey: 3,
            path: @"\nested\file.txt");

        Activity activity = Assert.Single(
            stopped
                .ToArray()
                .Where(candidate => candidate.GetTagItem("cfsharp.operation") as string ==
                    "CloudProviderSession.FetchData"));
        Assert.Equal("CloudProviderSession.FetchData", activity.GetTagItem("cfsharp.operation"));
        Assert.Equal(1L, activity.GetTagItem("cfsharp.provider.connection_key"));
        Assert.Equal(2L, activity.GetTagItem("cfsharp.provider.transfer_key"));
        Assert.Equal(3L, activity.GetTagItem("cfsharp.provider.request_key"));
        Assert.Equal("IOException", activity.GetTagItem("cfsharp.error.type"));
        Assert.Equal(new IOException().HResult, activity.GetTagItem("cfsharp.error.hresult"));
        Assert.NotEqual(@"\nested\file.txt", activity.GetTagItem("cfsharp.provider.path_fingerprint"));
    }
}
