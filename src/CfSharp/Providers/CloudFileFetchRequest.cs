namespace CfSharp;

/// <summary>Contains an immutable snapshot of one Windows file-content request.</summary>
/// <remarks>
/// CfSharp copies all callback-backed values before creating this object. Instances own their
/// identity memory, contain no native pointers, and are safe for concurrent reads.
/// </remarks>
public sealed class CloudFileFetchRequest
{
    private readonly byte[] _fileIdentity;

    internal CloudFileFetchRequest(
        string normalizedPath,
        byte[] fileIdentity,
        long fileSize,
        long offset,
        long length,
        CloudProviderProgressReporter? progress = null,
        Func<CloudPlaceholderSpec, bool, ValueTask>? restartHydration = null)
    {
        NormalizedPath = normalizedPath;
        _fileIdentity = (byte[])fileIdentity.Clone();
        FileSize = fileSize;
        Offset = offset;
        Length = length;
        Progress = progress;
        _restartHydration = restartHydration;
    }

    /// <summary>Gets the normalized callback path supplied by Windows.</summary>
    public string NormalizedPath { get; }

    /// <summary>Gets a provider-defined identity copied from the placeholder.</summary>
    public ReadOnlySpan<byte> FileIdentity => _fileIdentity;

    /// <summary>Gets the logical file size reported by Windows.</summary>
    public long FileSize { get; }

    /// <summary>Gets the starting offset of the required range.</summary>
    public long Offset { get; }

    /// <summary>Gets the length in bytes of the required range.</summary>
    public long Length { get; }

    /// <summary>Gets the optional best-effort progress reporter for this request.</summary>
    /// <remarks>
    /// The reporter is null only for requests constructed by internal test or compatibility
    /// paths. Provider callbacks created by <see cref="CloudProviderSession"/> always expose it.
    /// </remarks>
    public CloudProviderProgressReporter? Progress { get; }

    private readonly Func<CloudPlaceholderSpec, bool, ValueTask>? _restartHydration;

    /// <summary>
    /// Requests a guarded native hydration restart using replacement metadata and identity.
    /// </summary>
    /// <param name="replacement">Replacement file placeholder metadata and identity.</param>
    /// <param name="markInSync">Whether the restarted placeholder should be marked in sync.</param>
    /// <returns>An operation that completes when Windows accepts the restart.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="replacement"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The request was not created by an active provider session.
    /// </exception>
    /// <exception cref="CloudFilesException">Windows rejects the restart.</exception>
    public ValueTask RestartHydrationAsync(
        CloudPlaceholderSpec replacement,
        bool markInSync = true)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return _restartHydration is null
            ? ValueTask.FromException(new InvalidOperationException(
                "Hydration restart is available only for active Cloud Files provider requests."))
            : _restartHydration(replacement, markInSync);
    }
}
