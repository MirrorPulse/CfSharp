using System.Runtime.Versioning;

namespace CfSharp;

/// <summary>Represents an immutable, path-bound item within one cloud file system.</summary>
/// <remarks>
/// This object contains only its owning facade, canonical paths, and expected item kind. It owns
/// no native handle and caches no mutable file-system or durable state. Property reads are safe
/// from concurrent threads. Operations require the owning <see cref="CloudFileSystem"/> to remain
/// started.
/// </remarks>
[SupportedOSPlatform("windows10.0.16299")]
public abstract class CloudItem
{
    private readonly CloudFileSystem _owner;

    private protected CloudItem(
        CloudFileSystem owner,
        string fullPath,
        string relativePath,
        CloudItemKind kind)
    {
        _owner = owner;
        FullPath = fullPath;
        RelativePath = relativePath;
        Kind = kind;
    }

    /// <summary>Gets whether this reference expects a file or directory.</summary>
    public CloudItemKind Kind { get; }

    /// <summary>Gets the canonical absolute local path.</summary>
    public string FullPath { get; }

    /// <summary>Gets the canonical path relative to the owning sync root.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the final path component, or the root directory name for the root item.</summary>
    public string Name => Path.GetFileName(FullPath);

    /// <summary>Gets the immutable parent-directory reference, or null for the sync root.</summary>
    public CloudDirectory? Parent
    {
        get
        {
            if (RelativePath.Length == 0)
            {
                return null;
            }

            string? parent = Path.GetDirectoryName(RelativePath);
            return _owner.GetDirectory(parent ?? string.Empty);
        }
    }

    /// <summary>Reads fresh local and durable state without retaining a native handle.</summary>
    /// <param name="cancellationToken">Token that cancels state-store access.</param>
    /// <returns>A new immutable snapshot. Missing items are represented explicitly.</returns>
    /// <exception cref="InvalidOperationException">
    /// The owning file system has not started, or the existing item has another kind.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The owning file system is stopping or disposed.</exception>
    /// <exception cref="CloudFilesException">Windows cannot inspect Cloud Files metadata.</exception>
    public ValueTask<CloudItemSnapshot> InspectAsync(
        CancellationToken cancellationToken = default) =>
        _owner.InspectAsync(this, cancellationToken);

    /// <inheritdoc/>
    public override string ToString() => FullPath;
}

/// <summary>Represents an immutable path-bound file reference.</summary>
[SupportedOSPlatform("windows10.0.16299")]
public sealed class CloudFile : CloudItem
{
    internal CloudFile(CloudFileSystem owner, string fullPath, string relativePath)
        : base(owner, fullPath, relativePath, CloudItemKind.File)
    {
    }
}

/// <summary>Represents an immutable path-bound directory reference.</summary>
[SupportedOSPlatform("windows10.0.16299")]
public sealed class CloudDirectory : CloudItem
{
    internal CloudDirectory(CloudFileSystem owner, string fullPath, string relativePath)
        : base(owner, fullPath, relativePath, CloudItemKind.Directory)
    {
    }
}
