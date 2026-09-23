using System.Diagnostics;

namespace CfSharp;

/// <summary>Provides opt-in BCL diagnostics sources owned by CfSharp.</summary>
/// <remarks>
/// CfSharp does not configure exporters, loggers, or listeners. When no listener is attached the
/// implementation keeps the fast path allocation-light. Tags must remain low-cardinality and
/// must never contain paths, identities, content, credentials, or device identifiers.
/// </remarks>
public static class CloudDiagnostics
{
    /// <summary>Stable <see cref="ActivitySource"/> name.</summary>
    public const string ActivitySourceName = "CfSharp";

    /// <summary>Stable <see cref="Meter"/> name.</summary>
    public const string MeterName = "CfSharp";

    /// <summary>Gets the shared activity source used by the library.</summary>
    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);

    /// <summary>Gets the shared meter used by the library.</summary>
    public static System.Diagnostics.Metrics.Meter Meter { get; } =
        new(MeterName, typeof(CloudDiagnostics).Assembly.GetName().Version?.ToString());

    private static readonly System.Diagnostics.Metrics.Counter<long> WorkItemsEnqueued =
        Meter.CreateCounter<long>("cfsharp.provider.work.enqueued");
    private static readonly System.Diagnostics.Metrics.Counter<long> WorkItemsRejected =
        Meter.CreateCounter<long>("cfsharp.provider.work.rejected");
    private static readonly System.Diagnostics.Metrics.Counter<long> NativeFailures =
        Meter.CreateCounter<long>("cfsharp.native.failures");

    internal static Activity? StartActivity(string name, CloudProviderRequestKind kind)
    {
        try
        {
            Activity? activity = ActivitySource.StartActivity(name, ActivityKind.Internal);
            activity?.SetTag("cfsharp.request.kind", kind.ToString());
            return activity;
        }
        catch
        {
            return null;
        }
    }

    internal static void StopActivity(Activity? activity, string outcome)
    {
        if (activity is null)
        {
            return;
        }

        try
        {
            activity.SetTag("cfsharp.outcome", outcome);
            activity.Stop();
        }
        catch
        {
            // Listener failures must never cross a provider or callback boundary.
        }
    }

    internal static void RecordWorkItemEnqueued(CloudProviderRequestKind kind, bool accepted)
    {
        try
        {
            (accepted ? WorkItemsEnqueued : WorkItemsRejected).Add(
                1,
                new KeyValuePair<string, object?>("cfsharp.request.kind", kind.ToString()));
        }
        catch
        {
            // Meter listeners are diagnostic only.
        }
    }

    internal static void RecordNativeFailure(string operation)
    {
        try
        {
            NativeFailures.Add(1, new KeyValuePair<string, object?>("cfsharp.operation", operation));
        }
        catch
        {
            // Meter listeners are diagnostic only.
        }
    }

    internal static void RecordException(Activity? activity, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (activity is null)
        {
            return;
        }

        try
        {
            activity.SetTag("cfsharp.error.type", exception.GetType().Name);
            activity.SetTag("cfsharp.error.hresult", exception.HResult);
        }
        catch
        {
            // Diagnostics must never alter the primary operation result.
        }
    }

}
