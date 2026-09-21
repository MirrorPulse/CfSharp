namespace CfSharp;

/// <summary>Describes immutable file-system metadata for placeholder creation or update.</summary>
/// <remarks>
/// Timestamps are normalized to UTC. The value contains no native pointers and is safe for
/// concurrent reads. Use the kind-specific builder so directory attributes cannot be applied to a
/// file or omitted from a directory.
/// </remarks>
public sealed class CloudPlaceholderMetadata
{
    private CloudPlaceholderMetadata(
        CloudItemKind kind,
        FileAttributes attributes,
        DateTimeOffset? creationTime,
        DateTimeOffset? lastAccessTime,
        DateTimeOffset? lastWriteTime,
        DateTimeOffset? changeTime)
    {
        Kind = kind;
        Attributes = attributes;
        CreationTime = creationTime?.ToUniversalTime();
        LastAccessTime = lastAccessTime?.ToUniversalTime();
        LastWriteTime = lastWriteTime?.ToUniversalTime();
        ChangeTime = changeTime?.ToUniversalTime();
    }

    /// <summary>Gets whether this metadata describes a file or directory.</summary>
    public CloudItemKind Kind { get; }

    /// <summary>Gets the Win32 file attributes.</summary>
    public FileAttributes Attributes { get; }

    /// <summary>Gets the optional UTC creation time.</summary>
    public DateTimeOffset? CreationTime { get; }

    /// <summary>Gets the optional UTC last-access time.</summary>
    public DateTimeOffset? LastAccessTime { get; }

    /// <summary>Gets the optional UTC last-write time.</summary>
    public DateTimeOffset? LastWriteTime { get; }

    /// <summary>Gets the optional UTC file-system change time.</summary>
    public DateTimeOffset? ChangeTime { get; }

    /// <summary>Creates a builder for file metadata.</summary>
    public static Builder CreateFileBuilder() => new(CloudItemKind.File);

    /// <summary>Creates a builder for directory metadata.</summary>
    public static Builder CreateDirectoryBuilder() => new(CloudItemKind.Directory);

    /// <summary>Builds immutable placeholder metadata.</summary>
    public sealed class Builder
    {
        private readonly CloudItemKind _kind;
        private FileAttributes _attributes;
        private DateTimeOffset? _creationTime;
        private DateTimeOffset? _lastAccessTime;
        private DateTimeOffset? _lastWriteTime;
        private DateTimeOffset? _changeTime;

        internal Builder(CloudItemKind kind)
        {
            _kind = kind;
            _attributes = kind is CloudItemKind.Directory
                ? FileAttributes.Directory
                : FileAttributes.Normal;
        }

        /// <summary>Sets Win32 attributes appropriate for the selected item kind.</summary>
        public Builder WithAttributes(FileAttributes attributes)
        {
            _attributes = attributes;
            return this;
        }

        /// <summary>Sets the creation time.</summary>
        public Builder WithCreationTime(DateTimeOffset value)
        {
            _creationTime = value;
            return this;
        }

        /// <summary>Sets the last-access time.</summary>
        public Builder WithLastAccessTime(DateTimeOffset value)
        {
            _lastAccessTime = value;
            return this;
        }

        /// <summary>Sets the last-write time.</summary>
        public Builder WithLastWriteTime(DateTimeOffset value)
        {
            _lastWriteTime = value;
            return this;
        }

        /// <summary>Sets the file-system change time.</summary>
        public Builder WithChangeTime(DateTimeOffset value)
        {
            _changeTime = value;
            return this;
        }

        /// <summary>Creates the immutable metadata value.</summary>
        /// <exception cref="ArgumentException">
        /// Directory attributes disagree with the builder's item kind.
        /// </exception>
        public CloudPlaceholderMetadata Build()
        {
            bool directoryAttribute = _attributes.HasFlag(FileAttributes.Directory);
            if (directoryAttribute != (_kind is CloudItemKind.Directory))
            {
                throw new ArgumentException(
                    _kind is CloudItemKind.Directory
                        ? "Directory placeholder metadata must include the Directory attribute."
                        : "File placeholder metadata cannot include the Directory attribute.",
                    nameof(_attributes));
            }

            ValidateFileTime(_creationTime, nameof(_creationTime));
            ValidateFileTime(_lastAccessTime, nameof(_lastAccessTime));
            ValidateFileTime(_lastWriteTime, nameof(_lastWriteTime));
            ValidateFileTime(_changeTime, nameof(_changeTime));

            return new CloudPlaceholderMetadata(
                _kind,
                _attributes,
                _creationTime,
                _lastAccessTime,
                _lastWriteTime,
                _changeTime);
        }

        private static void ValidateFileTime(DateTimeOffset? value, string parameterName)
        {
            if (value is null)
            {
                return;
            }

            try
            {
                _ = value.Value.ToFileTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "The timestamp cannot be represented as a Windows file time.");
            }
        }
    }
}
