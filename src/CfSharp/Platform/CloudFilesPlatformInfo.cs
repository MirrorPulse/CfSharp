namespace CfSharp;

/// <summary>
/// Represents an immutable snapshot of the installed Windows Cloud Files platform.
/// </summary>
/// <remarks>
/// This value owns no native resources and is safe to copy, retain, and read concurrently.
/// Platform capabilities are monotonic with <see cref="IntegrationNumber"/>; build and revision
/// numbers primarily identify servicing updates.
/// </remarks>
public readonly record struct CloudFilesPlatformInfo
{
    /// <summary>Minimum Windows build for the core Cloud Files API.</summary>
    public const uint MinimumCoreWindowsBuild = 16299;

    /// <summary>Windows build that introduced rich sync-root status reporting.</summary>
    public const uint RichStatusMinimumWindowsBuild = 17134;

    /// <summary>Windows build that introduced provider progress V2.</summary>
    public const uint ProviderProgressV2MinimumWindowsBuild = 17763;

    /// <summary>Minimum integration number for placeholder management policy extensions.</summary>
    public const uint PlaceholderManagementPolicyMinimumIntegration = 784;

    /// <summary>Minimum integration number for full-restart hydration.</summary>
    public const uint FullRestartHydrationMinimumIntegration = 1280;

    /// <summary>Minimum integration number for force-convert placeholder flags.</summary>
    public const uint ForceConvertToCloudFileMinimumIntegration = 1280;

    /// <summary>Minimum integration number for callback-key hydration range information.</summary>
    public const uint PlaceholderRangeInfoForHydrationMinimumIntegration = 1536;

    /// <summary>
    /// Initializes a platform information value.
    /// </summary>
    /// <param name="buildNumber">The Windows Cloud Files platform build number.</param>
    /// <param name="revisionNumber">The Windows Cloud Files platform revision number.</param>
    /// <param name="integrationNumber">The monotonic platform capability level.</param>
    public CloudFilesPlatformInfo(uint buildNumber, uint revisionNumber, uint integrationNumber)
    {
        BuildNumber = buildNumber;
        RevisionNumber = revisionNumber;
        IntegrationNumber = integrationNumber;
    }

    /// <summary>
    /// Gets the build number of the installed Cloud Files platform.
    /// </summary>
    public uint BuildNumber { get; }

    /// <summary>
    /// Gets the revision number of the installed Cloud Files platform.
    /// </summary>
    public uint RevisionNumber { get; }

    /// <summary>
    /// Gets the monotonically increasing Cloud Files platform capability level.
    /// </summary>
    public uint IntegrationNumber { get; }

    /// <summary>
    /// Determines whether the platform meets a minimum integration level.
    /// </summary>
    /// <param name="minimumIntegrationNumber">The minimum required integration level.</param>
    /// <returns>
    /// <see langword="true"/> when <see cref="IntegrationNumber"/> is greater than or equal to
    /// <paramref name="minimumIntegrationNumber"/>; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This method performs a value comparison only. It does not call Windows, allocate native
    /// resources, or independently verify a specific API contract.
    /// </remarks>
    public bool SupportsIntegration(uint minimumIntegrationNumber) =>
        IntegrationNumber >= minimumIntegrationNumber;

    /// <summary>
    /// Determines whether this platform snapshot satisfies a versioned Cloud Files capability.
    /// </summary>
    /// <param name="capability">The capability to evaluate.</param>
    /// <returns><see langword="true"/> when both the OS and integration gate are satisfied.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The capability value is unknown.</exception>
    /// <remarks>
    /// The snapshot is deliberately used instead of the process architecture. An x64 and an
    /// ARM64 process on the same Windows build have the same Cloud Files capability contract.
    /// </remarks>
    public bool Supports(CloudFilesCapability capability) => capability switch
    {
        CloudFilesCapability.RichSyncRootStatus => BuildNumber >= RichStatusMinimumWindowsBuild,
        CloudFilesCapability.ProviderProgressV2 => BuildNumber >= ProviderProgressV2MinimumWindowsBuild,
        CloudFilesCapability.PlaceholderManagementPolicy =>
            SupportsIntegration(PlaceholderManagementPolicyMinimumIntegration),
        CloudFilesCapability.FullRestartHydration =>
            SupportsIntegration(FullRestartHydrationMinimumIntegration),
        CloudFilesCapability.ForceConvertToCloudFile =>
            SupportsIntegration(ForceConvertToCloudFileMinimumIntegration),
        CloudFilesCapability.PlaceholderRangeInfoForHydration =>
            BuildNumber >= RichStatusMinimumWindowsBuild &&
            SupportsIntegration(PlaceholderRangeInfoForHydrationMinimumIntegration),
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown Cloud Files capability."),
    };
}
