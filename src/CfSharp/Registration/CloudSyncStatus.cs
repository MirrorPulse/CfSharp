namespace CfSharp;

/// <summary>Contains rich diagnostic status reported for a registered sync root.</summary>
/// <remarks>
/// The status is copied into a temporary native buffer only for the reporting call. CfSharp does
/// not retain the device identifier or write it to the durable synchronization store.
/// </remarks>
public sealed record CloudSyncStatus
{
    /// <summary>Gets or initializes the provider-defined status code.</summary>
    public uint Code { get; init; }

    /// <summary>Gets or initializes the human-readable status description.</summary>
    public required string Description { get; init; }

    /// <summary>Gets or initializes an opaque provider device identifier.</summary>
    public ReadOnlyMemory<byte> DeviceId { get; init; }
}
