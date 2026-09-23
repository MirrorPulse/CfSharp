namespace CfSharp;

/// <summary>Identifies the destination of a provider progress report.</summary>
public sealed record CloudProgressTarget
{
    private CloudProgressTarget(CloudProviderRequestId? requestId, uint targetSessionId)
    {
        RequestId = requestId;
        TargetSessionId = targetSessionId;
    }

    /// <summary>Gets a target that reports the current hydration request through V1.</summary>
    public static CloudProgressTarget CurrentHydrationRequest { get; } = new(null, 0);

    /// <summary>Creates a request/session target for the V2 progress API.</summary>
    /// <param name="requestId">Opaque request identifier copied from a callback.</param>
    /// <param name="targetSessionId">Windows target session, or zero for the default session.</param>
    public static CloudProgressTarget Request(CloudProviderRequestId requestId, uint targetSessionId = 0)
    {
        if (!requestId.IsValid)
        {
            throw new ArgumentException("A valid callback request identifier is required.", nameof(requestId));
        }

        return new CloudProgressTarget(requestId, targetSessionId);
    }

    /// <summary>Gets the optional request identifier.</summary>
    public CloudProviderRequestId? RequestId { get; }

    /// <summary>Gets the target Windows session identifier.</summary>
    public uint TargetSessionId { get; }

    internal bool IsCurrentHydration => RequestId is null;
}

/// <summary>Identifies a callback request without exposing the native key layout.</summary>
public readonly struct CloudProviderRequestId : IEquatable<CloudProviderRequestId>
{
    private readonly long _value;

    internal CloudProviderRequestId(long value)
    {
        _value = value;
    }

    internal bool IsValid => _value != 0;

    /// <inheritdoc/>
    public bool Equals(CloudProviderRequestId other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CloudProviderRequestId other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value.GetHashCode();

    /// <summary>Compares two provider request identifiers.</summary>
    public static bool operator ==(CloudProviderRequestId left, CloudProviderRequestId right) => left.Equals(right);

    /// <summary>Compares two provider request identifiers.</summary>
    public static bool operator !=(CloudProviderRequestId left, CloudProviderRequestId right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => IsValid ? $"request:{_value:x16}" : "request:default";

    internal long ToNativeValue() => _value;
}

/// <summary>Classifies the result of one best-effort progress report.</summary>
public enum CloudProgressReportState
{
    /// <summary>The native progress API accepted the report.</summary>
    Reported = 0,

    /// <summary>The report was coalesced by the monotonic rate limiter.</summary>
    Throttled = 1,

    /// <summary>The requested target is unavailable on this Windows version.</summary>
    Unsupported = 2,

    /// <summary>The native progress API returned a failure HRESULT.</summary>
    NativeFailure = 3,
}

/// <summary>Returns the outcome and native route of one progress report.</summary>
public readonly record struct CloudProgressReportResult(
    CloudProgressReportState State,
    bool UsedV2,
    int HResult)
{
    /// <summary>Gets whether the report reached a native progress API.</summary>
    public bool WasSent => State is CloudProgressReportState.Reported or CloudProgressReportState.NativeFailure;
}
