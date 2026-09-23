namespace CfSharp;

/// <summary>Reports monotonic, best-effort progress for one active provider request.</summary>
/// <remarks>
/// Reports are coalesced to avoid flooding the Cloud Files filter while a provider is producing
/// data. The first report and the terminal report are always forwarded when they are monotonic.
/// A native progress failure is intentionally ignored because progress is diagnostic and must not
/// replace the request's primary transfer result.
/// </remarks>
public sealed class CloudProviderProgressReporter
{
    private const long MinimumIntervalTicks = TimeSpan.TicksPerMillisecond * 250;
    private readonly Func<CloudProgressTarget, long, long, CloudProgressReportResult> _report;
    private readonly object _gate = new();
    private readonly Dictionary<CloudProgressTarget, ProgressState> _states = [];

    internal CloudProviderProgressReporter(Action<long, long> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _report = (_, completed, total) =>
        {
            report(completed, total);
            return new CloudProgressReportResult(
                CloudProgressReportState.Reported,
                UsedV2: false,
                HResult: 0);
        };
    }

    internal CloudProviderProgressReporter(
        Func<CloudProgressTarget, long, long, CloudProgressReportResult> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _report = report;
    }

    /// <summary>Reports completed work out of a fixed total.</summary>
    /// <param name="completed">Monotonic completed units.</param>
    /// <param name="total">Positive total units.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Completed or total is negative, total is zero, or completed exceeds total.
    /// </exception>
    public CloudProgressReportResult Report(long completed, long total) =>
        Report(CloudProgressTarget.CurrentHydrationRequest, completed, total);

    /// <summary>Reports progress to an explicit hydration request or V2 request/session target.</summary>
    /// <param name="target">Destination selected by the provider.</param>
    /// <param name="completed">Monotonic completed units.</param>
    /// <param name="total">Positive total units.</param>
    /// <returns>The native route and outcome of this report.</returns>
    public CloudProgressReportResult Report(
        CloudProgressTarget target,
        long completed,
        long total)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(completed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);
        if (completed > total)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completed),
                completed,
                "Completed work cannot exceed total work.");
        }

        lock (_gate)
        {
            if (!_states.TryGetValue(target, out ProgressState state))
            {
                state = new ProgressState { LastCompleted = -1 };
            }
            long previous = state.LastCompleted;
            if (completed < previous)
            {
                return RecordThrottled(target);
            }

            long now = DateTime.UtcNow.Ticks;
            if (completed != total && previous >= 0 &&
                now - state.LastReportedTimestamp < MinimumIntervalTicks)
            {
                state.LastCompleted = completed;
                _states[target] = state;
                return RecordThrottled(target);
            }

            state.LastCompleted = completed;
            state.LastReportedTimestamp = now;
            _states[target] = state;
            CloudProgressReportResult result = _report(target, completed, total);
            CloudDiagnostics.RecordProgress(result);
            return result;
        }
    }

    private static CloudProgressReportResult RecordThrottled(CloudProgressTarget target)
    {
        CloudProgressReportResult throttled = new(
            CloudProgressReportState.Throttled,
            UsedV2: target.RequestId is not null,
            HResult: 0);
        CloudDiagnostics.RecordProgress(throttled);
        return throttled;
    }

    private struct ProgressState
    {
        internal long LastCompleted;
        internal long LastReportedTimestamp;
    }
}
