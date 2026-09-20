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
}
