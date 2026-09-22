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
    private readonly Action<long, long> _report;
    private long _lastCompleted = -1;
    private long _lastReportedTimestamp;

    internal CloudProviderProgressReporter(Action<long, long> report)
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
    public void Report(long completed, long total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);
        if (completed > total)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completed),
                completed,
                "Completed work cannot exceed total work.");
        }

        long previous = Volatile.Read(ref _lastCompleted);
        if (completed < previous)
        {
            return;
        }

        long now = DateTime.UtcNow.Ticks;
        long lastTimestamp = Volatile.Read(ref _lastReportedTimestamp);
        if (completed != total && previous >= 0 && now - lastTimestamp < MinimumIntervalTicks)
        {
            Interlocked.Exchange(ref _lastCompleted, completed);
            return;
        }

        if (Interlocked.CompareExchange(ref _lastCompleted, completed, previous) != previous)
        {
            return;
        }

        Volatile.Write(ref _lastReportedTimestamp, now);
        _report(completed, total);
    }
}
