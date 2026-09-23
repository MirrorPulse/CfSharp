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
