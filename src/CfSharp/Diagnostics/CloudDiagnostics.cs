using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CfSharp;

/// <summary>Provides opt-in BCL diagnostics sources owned by CfSharp.</summary>
/// <remarks>
/// CfSharp does not configure exporters, loggers, or listeners. When no listener is attached the
/// implementation keeps the fast path allocation-light. Metric tags must remain low-cardinality
/// and must never contain paths, identities, content, credentials, or device identifiers.
/// Native-failure activities may contain opaque request/connection identifiers and a one-way path
/// fingerprint so a listener can correlate a failure without receiving the caller's path.
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
    private static readonly System.Diagnostics.Metrics.Counter<long> ProgressReports =
        Meter.CreateCounter<long>("cfsharp.provider.progress.reports");
    private static readonly System.Diagnostics.Metrics.Counter<long> LeaseLifetimes =
        Meter.CreateCounter<long>("cfsharp.item.leases");
    private static readonly System.Diagnostics.Metrics.Counter<long> TransferLifetimes =
        Meter.CreateCounter<long>("cfsharp.item.transfers");
    private static readonly System.Diagnostics.Metrics.Counter<long> FinalizerRecoveries =
        Meter.CreateCounter<long>("cfsharp.resource.finalizer_recoveries");

    internal static Activity? StartActivity(string name, CloudProviderRequestKind kind)
    {
        Activity? activity = StartActivity(name, kind.ToString());
        try
        {
            activity?.SetTag("cfsharp.request.kind", kind.ToString());
        }
        catch
        {
            // Diagnostic tags must never cross a provider boundary.
        }

        return activity;
    }

    internal static Activity? StartActivity(string name, string operation)
    {
        try
        {
            Activity? activity = ActivitySource.StartActivity(name, ActivityKind.Internal);
            activity?.SetTag("cfsharp.operation", operation);
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
        => RecordNativeFailure(operation, hresult: null);

    internal static void RecordNativeFailure(string operation, int? hresult)
    {
        try
        {
            if (hresult is int value)
            {
                NativeFailures.Add(
                    1,
                    new KeyValuePair<string, object?>("cfsharp.operation", operation),
                    new KeyValuePair<string, object?>("cfsharp.native.hresult", value));
            }
            else
            {
                NativeFailures.Add(1, new KeyValuePair<string, object?>("cfsharp.operation", operation));
            }
        }
        catch
        {
            // Meter listeners are diagnostic only.
        }
    }

    internal static void RecordNativeFailure(
        string operation,
        int? hresult,
        long connectionKey,
        long requestKey,
        string? path)
    {
        RecordNativeFailure(operation, hresult);

        Activity? activity = null;
        bool ownsActivity = false;
        try
        {
            activity = Activity.Current;
            if (activity is null)
            {
                activity = ActivitySource.StartActivity(
                    "cfsharp.native.failure",
                    ActivityKind.Internal);
                ownsActivity = activity is not null;
            }

            if (activity is null)
            {
                return;
            }

            activity.SetTag("cfsharp.operation", operation);
            activity.SetTag("cfsharp.native.connection_key", connectionKey);
            activity.SetTag("cfsharp.native.request_key", requestKey);
            if (hresult is int value)
            {
                activity.SetTag("cfsharp.native.hresult", value);
            }

            if (!string.IsNullOrEmpty(path))
            {
                activity.SetTag("cfsharp.native.path_fingerprint", FingerprintPath(path));
            }
        }
        catch
        {
            // Diagnostic listeners must never alter the native completion result.
        }
        finally
        {
            if (ownsActivity)
            {
                StopActivity(activity, "failed");
            }
        }
    }

    internal static void RecordProviderFailure(
        string operation,
        Exception exception,
        long connectionKey,
        long transferKey,
        long requestKey,
        string? path)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Activity? activity = null;
        bool ownsActivity = false;
        try
        {
            activity = Activity.Current;
            if (activity is null)
            {
                activity = ActivitySource.StartActivity(
                    "cfsharp.provider.failure",
                    ActivityKind.Internal);
                ownsActivity = activity is not null;
            }

            if (activity is null)
            {
                return;
            }

            activity.SetTag("cfsharp.operation", operation);
            activity.SetTag("cfsharp.provider.connection_key", connectionKey);
            activity.SetTag("cfsharp.provider.transfer_key", transferKey);
            activity.SetTag("cfsharp.provider.request_key", requestKey);
            RecordException(activity, exception);
            if (!string.IsNullOrEmpty(path))
            {
                activity.SetTag("cfsharp.provider.path_fingerprint", FingerprintPath(path));
            }
        }
        catch
        {
            // Diagnostic listeners must never alter the provider completion result.
        }
        finally
        {
            if (ownsActivity)
            {
                StopActivity(activity, "failed");
            }
        }
    }

    private static string FingerprintPath(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];

    internal static void RecordProgress(CloudProgressReportResult result)
    {
        try
        {
            ProgressReports.Add(
                1,
                new KeyValuePair<string, object?>("cfsharp.progress.state", result.State.ToString()),
                new KeyValuePair<string, object?>("cfsharp.progress.route", result.UsedV2 ? "v2" : "v1"));
        }
        catch
        {
            // Meter listeners are diagnostic only.
        }
    }

    internal static void RecordLeaseLifetime(bool created)
    {
        try
        {
            LeaseLifetimes.Add(
                1,
                new KeyValuePair<string, object?>("cfsharp.lifecycle", created ? "created" : "disposed"));
        }
        catch
        {
            // Meter listeners are diagnostic only.
        }
    }

    internal static void RecordTransferLifetime(bool created)
    {
        try
        {
            TransferLifetimes.Add(
                1,
                new KeyValuePair<string, object?>("cfsharp.lifecycle", created ? "created" : "disposed"));
        }
        catch
        {
            // Meter listeners are diagnostic only.
        }
    }

    internal static void RecordFinalizerRecovery(string resource, bool recovered)
    {
        try
        {
            FinalizerRecoveries.Add(
                1,
                new KeyValuePair<string, object?>("cfsharp.resource", resource),
                new KeyValuePair<string, object?>(
                    "cfsharp.recovery", recovered ? "success" : "failure"));
        }
        catch
        {
            // Finalizer diagnostics must never throw on the finalizer thread.
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
