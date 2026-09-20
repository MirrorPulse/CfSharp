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
        long length)
    {
        NormalizedPath = normalizedPath;
        _fileIdentity = (byte[])fileIdentity.Clone();
        FileSize = fileSize;
        Offset = offset;
        Length = length;
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
}
