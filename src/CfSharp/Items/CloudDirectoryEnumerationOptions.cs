using System.Diagnostics.CodeAnalysis;

namespace CfSharp;

/// <summary>Selects which local item kinds a directory enumeration returns.</summary>
[Flags]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The plural name describes a selectable set of item kinds.")]
public enum CloudDirectoryEntryKinds
{
    /// <summary>Returns no entries.</summary>
    None = 0,

    /// <summary>Returns file entries.</summary>
    Files = 1,

    /// <summary>Returns directory entries.</summary>
    Directories = 2,

    /// <summary>Returns both files and directories.</summary>
    All = Files | Directories,
}

/// <summary>Controls deterministic ordering within each enumerated local directory.</summary>
public enum CloudDirectoryEnumerationOrder
{
    /// <summary>Preserves the order supplied by the Windows file system.</summary>
    FileSystem = 0,

    /// <summary>Orders entries by name using ordinal, case-insensitive Windows semantics.</summary>
    NameAscending = 1,

    /// <summary>Orders entries by name in descending ordinal, case-insensitive order.</summary>
    NameDescending = 2,
}

/// <summary>Provides immutable options for local directory enumeration.</summary>
/// <remarks>
/// Local enumeration never queries a remote provider and never creates placeholders. Recursive
/// enumeration is breadth-first and does not follow directory reparse points, preventing cycles
/// and traversal outside the sync root. Ordering applies independently within each directory.
/// </remarks>
public sealed class CloudDirectoryEnumerationOptions
{
    private CloudDirectoryEnumerationOptions(Builder builder)
    {
        SearchPattern = builder.SearchPattern;
        Recursive = builder.Recursive;
        EntryKinds = builder.EntryKinds;
        Order = builder.Order;
    }

    /// <summary>Gets default top-level enumeration of all entries in file-system order.</summary>
    public static CloudDirectoryEnumerationOptions Default { get; } = CreateBuilder().Build();

    /// <summary>Gets the simple file-name pattern applied to returned entries.</summary>
    public string SearchPattern { get; }

    /// <summary>Gets whether descendant directories are enumerated breadth-first.</summary>
    public bool Recursive { get; }

    /// <summary>Gets the item kinds included in results.</summary>
    public CloudDirectoryEntryKinds EntryKinds { get; }

    /// <summary>Gets the ordering applied within each visited directory.</summary>
    public CloudDirectoryEnumerationOrder Order { get; }

    /// <summary>Creates a mutable builder initialized with conservative local defaults.</summary>
    /// <returns>A new builder.</returns>
    public static Builder CreateBuilder() => new();

    /// <summary>Builds immutable local enumeration options.</summary>
    /// <remarks>A builder is mutable and not thread-safe.</remarks>
    public sealed class Builder
    {
        internal string SearchPattern { get; private set; } = "*";

        internal bool Recursive { get; private set; }

        internal CloudDirectoryEntryKinds EntryKinds { get; private set; } =
            CloudDirectoryEntryKinds.All;

        internal CloudDirectoryEnumerationOrder Order { get; private set; } =
            CloudDirectoryEnumerationOrder.FileSystem;

        /// <summary>Sets a simple file-name pattern such as <c>*.txt</c>.</summary>
        /// <param name="searchPattern">
        /// Non-empty pattern without directory separators. Matching is ordinal and case-insensitive.
        /// </param>
        /// <returns>This builder.</returns>
        public Builder WithSearchPattern(string searchPattern)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
            if (searchPattern.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                searchPattern.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The search pattern cannot contain directory separators.",
                    nameof(searchPattern));
            }

            SearchPattern = searchPattern;
            return this;
        }

        /// <summary>Sets whether enumeration visits descendant directories.</summary>
        /// <param name="recursive"><see langword="true"/> for breadth-first recursion.</param>
        /// <returns>This builder.</returns>
        public Builder WithRecursion(bool recursive = true)
        {
            Recursive = recursive;
            return this;
        }

        /// <summary>Sets which local item kinds are returned.</summary>
        /// <param name="entryKinds">Any defined combination of files and directories.</param>
        /// <returns>This builder.</returns>
        public Builder WithEntryKinds(CloudDirectoryEntryKinds entryKinds)
        {
            EntryKinds = entryKinds;
            return this;
        }

        /// <summary>Sets entry ordering within each visited directory.</summary>
        /// <param name="order">The requested local ordering.</param>
        /// <returns>This builder.</returns>
        public Builder WithOrder(CloudDirectoryEnumerationOrder order)
        {
            Order = order;
            return this;
        }

        /// <summary>Validates and creates immutable enumeration options.</summary>
        /// <returns>The immutable options.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Entry kinds contain unknown bits or the order is undefined.
        /// </exception>
        public CloudDirectoryEnumerationOptions Build()
        {
            const CloudDirectoryEntryKinds validKinds = CloudDirectoryEntryKinds.All;
            if ((EntryKinds & ~validKinds) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(EntryKinds), EntryKinds, null);
            }

            if (!Enum.IsDefined(Order))
            {
                throw new ArgumentOutOfRangeException(nameof(Order), Order, null);
            }

            return new CloudDirectoryEnumerationOptions(this);
        }
    }
}
