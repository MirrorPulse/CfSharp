using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace CfSharp;

/// <summary>Describes one page request against an application-owned remote catalog.</summary>
/// <remarks>
/// The query is a value object: the continuation bytes are copied and never interpreted by
/// CfSharp. A catalog implementation owns the meaning of its cursor and remains responsible for
/// transport, authentication, retries, and remote consistency. The requested path is relative to
/// the managed sync root and the result must contain only immediate children of that path.
/// </remarks>
public sealed class CloudRemoteDirectoryQuery
{
    /// <summary>Initializes a validated remote directory query.</summary>
    /// <param name="relativePath">Remote directory path, or an empty string for the root.</param>
    /// <param name="pageSize">Maximum number of entries requested from the catalog.</param>
    /// <param name="continuationCursor">Opaque cursor returned by an earlier page.</param>
    /// <param name="entryKinds">File and/or directory kinds requested from the catalog.</param>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page size or entry kinds are invalid.</exception>
    public CloudRemoteDirectoryQuery(
        string relativePath = "",
        int pageSize = 256,
        ReadOnlyMemory<byte> continuationCursor = default,
        CloudDirectoryEntryKinds entryKinds = CloudDirectoryEntryKinds.All)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (relativePath.Length == 0)
        {
            RelativePath = string.Empty;
        }
        else
        {
            RelativePath = CloudRemotePathValidation.Canonicalize(relativePath, nameof(relativePath));
        }

        if (pageSize <= 0 || pageSize > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                pageSize,
                "A remote directory page size must be between 1 and 4096.");
        }

        const CloudDirectoryEntryKinds validKinds = CloudDirectoryEntryKinds.All;
        if ((entryKinds & ~validKinds) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryKinds), entryKinds, null);
        }

        PageSize = pageSize;
        _continuationCursor = continuationCursor.ToArray();
        EntryKinds = entryKinds;
    }

    private readonly byte[] _continuationCursor;

    /// <summary>Gets the canonical remote directory path, or empty for the sync-root directory.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the maximum number of entries requested for this page.</summary>
    public int PageSize { get; }

    /// <summary>Gets the opaque provider cursor used to continue a previous page.</summary>
    public ReadOnlyMemory<byte> ContinuationCursor => _continuationCursor;

    /// <summary>Gets the item kinds requested from the remote catalog.</summary>
    public CloudDirectoryEntryKinds EntryKinds { get; }
}

/// <summary>Represents immutable remote metadata for one directory child.</summary>
public sealed class CloudRemoteDirectoryEntry
{
    /// <summary>Initializes one remote directory entry.</summary>
    /// <param name="remoteId">Provider-stable remote object identifier.</param>
    /// <param name="remoteRevision">Opaque provider revision for the entry.</param>
    /// <param name="itemKind">Whether the entry is a file or directory.</param>
    /// <param name="relativePath">Canonical path relative to the sync root.</param>
    /// <param name="itemId">Known CfSharp identity, when one is available.</param>
    /// <param name="length">Logical file length, or null for directories and tombstones.</param>
    /// <param name="metadata">Remote file-system metadata, when available.</param>
    /// <param name="isDeleted">Whether the entry represents a remote tombstone.</param>
    /// <exception cref="ArgumentException">An identifier or path is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A length or item kind is invalid.</exception>
    public CloudRemoteDirectoryEntry(
        string remoteId,
        string remoteRevision,
        CloudItemKind itemKind,
        string relativePath,
        Guid? itemId = null,
        long? length = null,
        CloudPlaceholderMetadata? metadata = null,
        bool isDeleted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteId, nameof(remoteId));
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteRevision, nameof(remoteRevision));
        if (!Enum.IsDefined(itemKind))
        {
            throw new ArgumentOutOfRangeException(nameof(itemKind), itemKind, null);
        }

        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("The item identifier cannot be empty when supplied.", nameof(itemId));
        }

        if (length < 0 || (itemKind is CloudItemKind.Directory && length is not null))
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The entry length is invalid.");
        }

        if (metadata is not null && metadata.Kind != itemKind)
        {
            throw new ArgumentException("Remote metadata must match the entry kind.", nameof(metadata));
        }

        RemoteId = remoteId;
        RemoteRevision = remoteRevision;
        ItemKind = itemKind;
        RelativePath = CloudRemotePathValidation.Canonicalize(relativePath, nameof(relativePath));
        ItemId = itemId;
        Length = length;
        Metadata = metadata;
        IsDeleted = isDeleted;
    }

    /// <summary>Gets the provider-stable object identifier.</summary>
    public string RemoteId { get; }

    /// <summary>Gets the opaque remote revision.</summary>
    public string RemoteRevision { get; }

    /// <summary>Gets whether the entry is a file or directory.</summary>
    public CloudItemKind ItemKind { get; }

    /// <summary>Gets the canonical path relative to the managed sync root.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the final path component.</summary>
    public string Name => Path.GetFileName(RelativePath);

    /// <summary>Gets the known CfSharp identity, when supplied by the catalog.</summary>
    public Guid? ItemId { get; }

    /// <summary>Gets the logical file length, when this entry is a file.</summary>
    public long? Length { get; }

    /// <summary>Gets immutable remote file-system metadata, when supplied.</summary>
    public CloudPlaceholderMetadata? Metadata { get; }

    /// <summary>Gets whether this entry is a remote deletion tombstone.</summary>
    public bool IsDeleted { get; }
}

/// <summary>Contains one deterministic page returned by a remote directory catalog.</summary>
public sealed class CloudRemoteDirectoryPage
{
    private readonly byte[] _continuationCursor;

    /// <summary>Initializes a page and validates deterministic path ordering.</summary>
    /// <param name="entries">Immediate-child entries ordered by canonical path.</param>
    /// <param name="continuationCursor">Opaque cursor for the next page, if any.</param>
    /// <param name="isComplete">Whether the catalog has no further page for this query.</param>
    /// <exception cref="ArgumentNullException">The entry sequence contains null.</exception>
    /// <exception cref="ArgumentException">Entries are duplicated or not deterministically ordered.</exception>
    public CloudRemoteDirectoryPage(
        IEnumerable<CloudRemoteDirectoryEntry> entries,
        ReadOnlyMemory<byte> continuationCursor = default,
        bool isComplete = true)
    {
        ArgumentNullException.ThrowIfNull(entries);
        List<CloudRemoteDirectoryEntry> materialized = entries.ToList();
        for (int index = 0; index < materialized.Count; index++)
        {
            ArgumentNullException.ThrowIfNull(materialized[index]);
            if (index == 0)
            {
                continue;
            }

            int comparison = StringComparer.OrdinalIgnoreCase.Compare(
                materialized[index - 1].RelativePath,
                materialized[index].RelativePath);
            if (comparison >= 0)
            {
                throw new ArgumentException(
                    "Remote directory entries must be unique and ordered by path.",
                    nameof(entries));
            }
        }

        Entries = new ReadOnlyCollection<CloudRemoteDirectoryEntry>(materialized);
        _continuationCursor = continuationCursor.ToArray();
        if (!isComplete && _continuationCursor.Length == 0)
        {
            throw new ArgumentException(
                "An incomplete remote directory page must provide a continuation cursor.",
                nameof(continuationCursor));
        }

        IsComplete = isComplete;
    }

    /// <summary>Gets the ordered immutable entries in this page.</summary>
    public IReadOnlyList<CloudRemoteDirectoryEntry> Entries { get; }

    /// <summary>Gets the opaque cursor for the next page.</summary>
    public ReadOnlyMemory<byte> ContinuationCursor => _continuationCursor;

    /// <summary>Gets whether this page completes the requested remote directory.</summary>
    public bool IsComplete { get; }
}

/// <summary>Reads remote metadata without creating local placeholders or performing native calls.</summary>
public interface ICloudRemoteDirectoryCatalog
{
    /// <summary>Reads one deterministic page from the application-owned remote catalog.</summary>
    /// <param name="query">Validated path, page-size, kind, and opaque cursor request.</param>
    /// <param name="cancellationToken">Token for the application-owned catalog operation.</param>
    /// <returns>A page whose entries are immediate children of <paramref name="query"/>.</returns>
    ValueTask<CloudRemoteDirectoryPage> ReadPageAsync(
        CloudRemoteDirectoryQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>Requests a paged, side-effect-free view of durable local synchronization state.</summary>
public sealed class CloudSynchronizedDirectoryQuery
{
    private readonly byte[] _continuationCursor;

    /// <summary>Initializes a synchronized-directory query.</summary>
    /// <param name="relativePath">Local directory path, or empty for the sync root.</param>
    /// <param name="pageSize">Maximum entries returned by one page.</param>
    /// <param name="continuationCursor">Cursor returned by a prior synchronized page.</param>
    /// <param name="entryKinds">Local file and/or directory kinds to include.</param>
    /// <param name="includeTombstones">Whether durable deleted entries are included.</param>
    public CloudSynchronizedDirectoryQuery(
        string relativePath = "",
        int pageSize = 256,
        ReadOnlyMemory<byte> continuationCursor = default,
        CloudDirectoryEntryKinds entryKinds = CloudDirectoryEntryKinds.All,
        bool includeTombstones = false)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (relativePath.Length == 0)
        {
            RelativePath = string.Empty;
        }
        else
        {
            RelativePath = CloudRemotePathValidation.Canonicalize(relativePath, nameof(relativePath));
        }

        if (pageSize <= 0 || pageSize > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, null);
        }

        const CloudDirectoryEntryKinds validKinds = CloudDirectoryEntryKinds.All;
        if ((entryKinds & ~validKinds) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryKinds), entryKinds, null);
        }

        PageSize = pageSize;
        _continuationCursor = continuationCursor.ToArray();
        EntryKinds = entryKinds;
        IncludeTombstones = includeTombstones;
    }

    /// <summary>Gets the canonical local directory path.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the maximum number of entries in one page.</summary>
    public int PageSize { get; }

    /// <summary>Gets the opaque local-view continuation cursor.</summary>
    public ReadOnlyMemory<byte> ContinuationCursor => _continuationCursor;

    /// <summary>Gets the local item kinds to include.</summary>
    public CloudDirectoryEntryKinds EntryKinds { get; }

    /// <summary>Gets whether durable tombstones should be included.</summary>
    public bool IncludeTombstones { get; }
}

/// <summary>Combines one materialized local child with its durable remote identity, when known.</summary>
public sealed class CloudSynchronizedDirectoryEntry
{
    internal CloudSynchronizedDirectoryEntry(
        string relativePath,
        CloudItemKind kind,
        CloudItemState? durableState,
        bool isMaterialized)
    {
        RelativePath = relativePath;
        Kind = kind;
        DurableState = durableState;
        IsMaterialized = isMaterialized;
    }

    /// <summary>Gets the canonical path relative to the sync root.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the final path component.</summary>
    public string Name => Path.GetFileName(RelativePath);

    /// <summary>Gets whether the entry is a file or directory.</summary>
    public CloudItemKind Kind { get; }

    /// <summary>Gets the durable identity and revision, when one is recorded.</summary>
    public CloudItemState? DurableState { get; }

    /// <summary>Gets whether the corresponding local namespace entry is materialized.</summary>
    public bool IsMaterialized { get; }

    /// <summary>Gets whether this entry is a durable deletion tombstone.</summary>
    public bool IsTombstone => DurableState?.IsTombstone is true;
}

/// <summary>Contains one deterministic page of synchronized local/durable directory entries.</summary>
public sealed class CloudSynchronizedDirectoryPage
{
    private readonly byte[] _continuationCursor;

    internal CloudSynchronizedDirectoryPage(
        IReadOnlyList<CloudSynchronizedDirectoryEntry> entries,
        ReadOnlyMemory<byte> continuationCursor,
        bool isComplete)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Any(static entry => entry is null))
        {
            throw new ArgumentException("A synchronized page cannot contain a null entry.", nameof(entries));
        }

        Entries = new ReadOnlyCollection<CloudSynchronizedDirectoryEntry>(entries.ToArray());
        _continuationCursor = continuationCursor.ToArray();
        IsComplete = isComplete;
    }

    /// <summary>Gets ordered entries in this page.</summary>
    public IReadOnlyList<CloudSynchronizedDirectoryEntry> Entries { get; }

    /// <summary>Gets the opaque cursor for the next local view page.</summary>
    public ReadOnlyMemory<byte> ContinuationCursor => _continuationCursor;

    /// <summary>Gets whether all matching entries have been returned.</summary>
    public bool IsComplete { get; }
}

internal static class CloudSynchronizedDirectoryCursor
{
    private const string Prefix = "cfsharp.synchronized-directory/v2|";

    internal static ReadOnlyMemory<byte> Create(int offset, ReadOnlySpan<byte> fingerprint) =>
        Encoding.UTF8.GetBytes(
            Prefix +
            offset.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "|" +
            Convert.ToHexString(fingerprint));

    internal static int Parse(
        ReadOnlySpan<byte> cursor,
        int maximum,
        ReadOnlySpan<byte> expectedFingerprint)
    {
        if (cursor.IsEmpty)
        {
            return 0;
        }

        string value = Encoding.UTF8.GetString(cursor);
        int fingerprintSeparator = value.IndexOf('|', Prefix.Length);
        if (!value.StartsWith(Prefix, StringComparison.Ordinal) ||
            fingerprintSeparator < Prefix.Length ||
            !int.TryParse(
                value.AsSpan(Prefix.Length, fingerprintSeparator - Prefix.Length),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int offset) ||
            offset < 0 ||
            offset > maximum ||
            !string.Equals(
                value[(fingerprintSeparator + 1)..],
                Convert.ToHexString(expectedFingerprint),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The synchronized-directory cursor is invalid or no longer matches the requested snapshot.",
                nameof(cursor));
        }

        return offset;
    }

    internal static byte[] CreateFingerprint(
        CloudSynchronizedDirectoryQuery query,
        IReadOnlyList<CloudSynchronizedDirectoryEntry> entries)
    {
        StringBuilder builder = new();
        builder.Append(query.RelativePath)
            .Append('\0')
            .Append(query.PageSize)
            .Append('\0')
            .Append((int)query.EntryKinds)
            .Append('\0')
            .Append(query.IncludeTombstones ? '1' : '0');
        foreach (CloudSynchronizedDirectoryEntry entry in entries)
        {
            builder.Append('\0')
                .Append(entry.RelativePath)
                .Append('|')
                .Append((int)entry.Kind)
                .Append('|')
                .Append(entry.DurableState?.ItemId.ToString("D") ?? string.Empty)
                .Append('|')
                .Append(entry.DurableState?.RemoteRevision ?? string.Empty)
                .Append('|')
                .Append(entry.IsMaterialized ? '1' : '0')
                .Append('|')
                .Append(entry.IsTombstone ? '1' : '0');
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
    }
}
