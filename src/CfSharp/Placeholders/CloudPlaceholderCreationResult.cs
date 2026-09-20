namespace CfSharp;

/// <summary>Describes a placeholder created beneath a sync root.</summary>
public sealed class CloudPlaceholderCreationResult
{
    internal CloudPlaceholderCreationResult(string path, long createUsn)
    {
        Path = path;
        CreateUsn = createUsn;
    }

    /// <summary>Gets the normalized absolute path of the created placeholder.</summary>
    public string Path { get; }

    /// <summary>Gets the update sequence number assigned by Windows during creation.</summary>
    public long CreateUsn { get; }
}
