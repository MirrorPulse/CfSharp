namespace CfSharp;

/// <summary>
/// Represents an immutable snapshot of a registered Cloud Files sync root.
/// </summary>
/// <remarks>
/// The value owns only managed memory and is safe for concurrent reads. It does not track
/// later registration updates or provider status changes; call <see cref="CloudSyncRoot.GetInfo"/>
/// again to obtain a fresh snapshot.
/// </remarks>
public sealed class CloudSyncRootInfo
{
    private readonly byte[] _syncRootIdentity;

    internal CloudSyncRootInfo(
        string path,
        long fileId,
        string providerName,
        string providerVersion,
        ReadOnlySpan<byte> syncRootIdentity,
        CloudHydrationPolicy hydrationPolicy,
        CloudHydrationPolicyModifiers hydrationModifiers,
        CloudPopulationPolicy populationPolicy,
        CloudInSyncPolicy inSyncPolicy,
        CloudHardLinkPolicy hardLinkPolicy,
        CloudProviderStatus providerStatus)
    {
        Path = path;
        FileId = fileId;
        ProviderName = providerName;
        ProviderVersion = providerVersion;
        _syncRootIdentity = syncRootIdentity.ToArray();
        HydrationPolicy = hydrationPolicy;
        HydrationModifiers = hydrationModifiers;
        PopulationPolicy = populationPolicy;
        InSyncPolicy = inSyncPolicy;
        HardLinkPolicy = hardLinkPolicy;
        ProviderStatus = providerStatus;
    }

    /// <summary>Gets the normalized absolute path used to query the sync root.</summary>
    public string Path { get; }

    /// <summary>Gets the volume-specific file identifier of the sync-root directory.</summary>
    public long FileId { get; }

    /// <summary>Gets the registered user-facing provider name.</summary>
    public string ProviderName { get; }

    /// <summary>Gets the registered user-facing provider version.</summary>
    public string ProviderVersion { get; }

    /// <summary>Gets a read-only view of the provider-defined sync-root identity.</summary>
    public ReadOnlySpan<byte> SyncRootIdentity => _syncRootIdentity;

    /// <summary>Gets the registered primary hydration policy.</summary>
    public CloudHydrationPolicy HydrationPolicy { get; }

    /// <summary>Gets the registered hydration-policy modifiers.</summary>
    public CloudHydrationPolicyModifiers HydrationModifiers { get; }

    /// <summary>Gets the registered namespace population policy.</summary>
    public CloudPopulationPolicy PopulationPolicy { get; }

    /// <summary>Gets the registered in-sync metadata tracking policy.</summary>
    public CloudInSyncPolicy InSyncPolicy { get; }

    /// <summary>Gets the registered hard-link policy.</summary>
    public CloudHardLinkPolicy HardLinkPolicy { get; }

    /// <summary>Gets the provider status reported by Windows when this snapshot was created.</summary>
    public CloudProviderStatus ProviderStatus { get; }
}
