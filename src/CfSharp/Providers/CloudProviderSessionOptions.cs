using System.Diagnostics.CodeAnalysis;

namespace CfSharp;

/// <summary>Configures the bounded callback runtime owned by a <see cref="CloudProviderSession"/>.</summary>
/// <remarks>
/// Values are immutable after a session has been connected. Queue and concurrency limits apply
/// before application provider code is entered, so a slow provider cannot create an unbounded
/// number of managed requests. The defaults are conservative for a Windows sync provider and
/// can be replaced for a measured workload.
/// </remarks>
public sealed record CloudProviderSessionOptions
{
    /// <summary>Gets the default callback-runtime configuration.</summary>
    public static CloudProviderSessionOptions Default { get; } = new();

    /// <summary>Gets the maximum number of callback envelopes waiting for a worker.</summary>
    public int QueueCapacity { get; init; } = 64;

    /// <summary>Gets the number of dispatcher workers that may run provider code.</summary>
    public int WorkerCount { get; init; } = 4;

    /// <summary>Gets the maximum number of concurrent file-data handlers.</summary>
    public int MaxConcurrentDataRequests { get; init; } = 4;

    /// <summary>Gets the maximum number of concurrent placeholder-page handlers.</summary>
    public int MaxConcurrentPlaceholderRequests { get; init; } = 1;

    /// <summary>Gets the maximum number of concurrent validation handlers.</summary>
    public int MaxConcurrentValidationRequests { get; init; } = 4;

    /// <summary>Gets the maximum number of concurrent policy and notification handlers.</summary>
    public int MaxConcurrentPolicyRequests { get; init; } = 4;

    /// <summary>
    /// Gets the pooled transfer chunk size. It must be a multiple of 4 KiB and no larger than 4 MiB.
    /// </summary>
    public int TransferChunkSize { get; init; } = 64 * 1024;

    /// <summary>Gets the bounded time allowed for cooperative session shutdown.</summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Requests full paths in copied callback requests.</summary>
    public bool RequireFullFilePath { get; init; } = true;

    /// <summary>Requests process information in native callback records.</summary>
    public bool RequireProcessInfo { get; init; }

    /// <summary>Blocks implicit hydration initiated by the provider process.</summary>
    public bool BlockSelfImplicitHydration { get; init; } = true;

    internal void Validate()
    {
        ValidatePositive(QueueCapacity, nameof(QueueCapacity));
        ValidatePositive(WorkerCount, nameof(WorkerCount));
        ValidatePositive(MaxConcurrentDataRequests, nameof(MaxConcurrentDataRequests));
        ValidatePositive(MaxConcurrentPlaceholderRequests, nameof(MaxConcurrentPlaceholderRequests));
        ValidatePositive(MaxConcurrentValidationRequests, nameof(MaxConcurrentValidationRequests));
        ValidatePositive(MaxConcurrentPolicyRequests, nameof(MaxConcurrentPolicyRequests));
        if (TransferChunkSize < 4096 || TransferChunkSize > 4 * 1024 * 1024 ||
            (TransferChunkSize % 4096) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TransferChunkSize),
                TransferChunkSize,
                "Transfer chunks must be aligned to 4 KiB and between 4 KiB and 4 MiB.");
        }

        if (ShutdownTimeout <= TimeSpan.Zero || ShutdownTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownTimeout),
                ShutdownTimeout,
                "Shutdown timeout must be positive and no longer than five minutes.");
        }
    }

    private static void ValidatePositive(int value, [NotNull] string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
    }
}
