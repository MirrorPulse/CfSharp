namespace CfSharp;

/// <summary>Describes one immutable child placeholder to create.</summary>
public abstract class CloudPlaceholderSpec
{
    private protected CloudPlaceholderSpec(
        string name,
        CloudItemKind kind,
        CloudPlaceholderIdentity identity,
        CloudPlaceholderMetadata metadata,
        bool initiallyInSync,
        CloudPlaceholderCollisionBehavior collisionBehavior)
    {
        Name = ValidateName(name);
        Kind = kind;
        Identity = identity;
        Metadata = metadata;
        InitiallyInSync = initiallyInSync;
        CollisionBehavior = collisionBehavior;
    }

    /// <summary>Gets the single child name relative to the receiving directory.</summary>
    public string Name { get; }

    /// <summary>Gets whether this specification creates a file or directory.</summary>
    public CloudItemKind Kind { get; }

    /// <summary>Gets the stable identity encoded into the native placeholder.</summary>
    public CloudPlaceholderIdentity Identity { get; }

    /// <summary>Gets immutable file-system metadata.</summary>
    public CloudPlaceholderMetadata Metadata { get; }

    /// <summary>Gets whether creation marks the placeholder in sync.</summary>
    public bool InitiallyInSync { get; }

    /// <summary>Gets explicit existing-item collision behavior.</summary>
    public CloudPlaceholderCollisionBehavior CollisionBehavior { get; }

    internal static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." ||
            name.EndsWith(' ') ||
            name.EndsWith('.') ||
            name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            IsReservedDeviceName(name))
        {
            throw new ArgumentException(
                "A cloud item name must be one valid child path segment.",
                nameof(name));
        }

        return name;
    }

    private static bool IsReservedDeviceName(string name)
    {
        ReadOnlySpan<char> stem = name.AsSpan(0, name.IndexOf('.') is int separator && separator >= 0
            ? separator
            : name.Length).TrimEnd();
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            IsNumberedDeviceName(stem, "COM") ||
            IsNumberedDeviceName(stem, "LPT");
    }

    private static bool IsNumberedDeviceName(ReadOnlySpan<char> name, string prefix) =>
        name.Length == 4 &&
        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        name[3] is >= '1' and <= '9';
}

/// <summary>Describes one file placeholder to create.</summary>
public sealed class CloudFilePlaceholderSpec : CloudPlaceholderSpec
{
    private CloudFilePlaceholderSpec(
        string name,
        CloudPlaceholderIdentity identity,
        CloudPlaceholderMetadata metadata,
        bool initiallyInSync,
        CloudPlaceholderCollisionBehavior collisionBehavior,
        long length,
        CloudAvailabilityTarget initialAvailability)
        : base(
            name,
            CloudItemKind.File,
            identity,
            metadata,
            initiallyInSync,
            collisionBehavior)
    {
        Length = length;
        InitialAvailability = initialAvailability;
    }

    /// <summary>Gets the non-negative logical file length.</summary>
    public long Length { get; }

    /// <summary>Gets the requested availability after creation completes.</summary>
    public CloudAvailabilityTarget InitialAvailability { get; }

    /// <summary>Creates a builder with a new stable CfSharp item identifier.</summary>
    public static Builder CreateBuilder(string name, string remoteId, long length) =>
        new(name, CloudPlaceholderIdentity.Create(remoteId), length);

    /// <summary>Creates a builder with an explicit stable identity.</summary>
    public static Builder CreateBuilder(
        string name,
        CloudPlaceholderIdentity identity,
        long length) => new(name, identity, length);

    /// <summary>Builds an immutable file placeholder specification.</summary>
    public sealed class Builder
    {
        private readonly string _name;
        private CloudPlaceholderIdentity _identity;
        private readonly long _length;
        private CloudPlaceholderMetadata _metadata = CloudPlaceholderMetadata.CreateFileBuilder().Build();
        private bool _initiallyInSync = true;
        private CloudPlaceholderCollisionBehavior _collisionBehavior;
        private CloudAvailabilityTarget _initialAvailability;

        internal Builder(string name, CloudPlaceholderIdentity identity, long length)
        {
            ArgumentNullException.ThrowIfNull(identity);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            _name = name;
            _identity = identity;
            _length = length;
        }

        /// <summary>Replaces the complete stable placeholder identity.</summary>
        public Builder WithIdentity(CloudPlaceholderIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            _identity = identity;
            return this;
        }

        /// <summary>Replaces the remote revision while retaining item and remote identifiers.</summary>
        public Builder WithRemoteRevision(string? revision)
        {
            _identity = new CloudPlaceholderIdentity(
                _identity.ItemId,
                _identity.RemoteId,
                revision);
            return this;
        }

        /// <summary>Sets file metadata.</summary>
        public Builder WithMetadata(CloudPlaceholderMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            if (metadata.Kind is not CloudItemKind.File)
            {
                throw new ArgumentException("File placeholder metadata is required.", nameof(metadata));
            }

            _metadata = metadata;
            return this;
        }

        /// <summary>Sets whether creation marks the file in sync.</summary>
        public Builder WithInSyncState(bool inSync)
        {
            _initiallyInSync = inSync;
            return this;
        }

        /// <summary>Sets explicit collision behavior.</summary>
        public Builder WithCollisionBehavior(CloudPlaceholderCollisionBehavior behavior)
        {
            _collisionBehavior = CloudPlaceholderConversionOptions.RequireDefined(
                behavior,
                nameof(behavior));
            return this;
        }

        /// <summary>Sets requested availability after creation.</summary>
        public Builder WithInitialAvailability(CloudAvailabilityTarget target)
        {
            _initialAvailability = CloudPlaceholderConversionOptions.RequireDefined(
                target,
                nameof(target));
            return this;
        }

        /// <summary>Creates the immutable file specification.</summary>
        public CloudFilePlaceholderSpec Build() => new(
            _name,
            _identity,
            _metadata,
            _initiallyInSync,
            _collisionBehavior,
            _length,
            _initialAvailability);
    }
}

/// <summary>Describes one directory placeholder to create.</summary>
public sealed class CloudDirectoryPlaceholderSpec : CloudPlaceholderSpec
{
    private CloudDirectoryPlaceholderSpec(
        string name,
        CloudPlaceholderIdentity identity,
        CloudPlaceholderMetadata metadata,
        bool initiallyInSync,
        CloudPlaceholderCollisionBehavior collisionBehavior,
        CloudDirectoryPopulationState populationState)
        : base(
            name,
            CloudItemKind.Directory,
            identity,
            metadata,
            initiallyInSync,
            collisionBehavior)
    {
        PopulationState = populationState;
    }

    /// <summary>Gets whether the new directory starts partial or complete.</summary>
    public CloudDirectoryPopulationState PopulationState { get; }

    /// <summary>Creates a builder with a new stable CfSharp item identifier.</summary>
    public static Builder CreateBuilder(string name, string remoteId) =>
        new(name, CloudPlaceholderIdentity.Create(remoteId));

    /// <summary>Creates a builder with an explicit stable identity.</summary>
    public static Builder CreateBuilder(string name, CloudPlaceholderIdentity identity) =>
        new(name, identity);

    /// <summary>Builds an immutable directory placeholder specification.</summary>
    public sealed class Builder
    {
        private readonly string _name;
        private CloudPlaceholderIdentity _identity;
        private CloudPlaceholderMetadata _metadata =
            CloudPlaceholderMetadata.CreateDirectoryBuilder().Build();
        private bool _initiallyInSync = true;
        private CloudPlaceholderCollisionBehavior _collisionBehavior;
        private CloudDirectoryPopulationState _populationState;

        internal Builder(string name, CloudPlaceholderIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            _name = name;
            _identity = identity;
        }

        /// <summary>Replaces the complete stable placeholder identity.</summary>
        public Builder WithIdentity(CloudPlaceholderIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            _identity = identity;
            return this;
        }

        /// <summary>Replaces the remote revision while retaining item and remote identifiers.</summary>
        public Builder WithRemoteRevision(string? revision)
        {
            _identity = new CloudPlaceholderIdentity(
                _identity.ItemId,
                _identity.RemoteId,
                revision);
            return this;
        }

        /// <summary>Sets directory metadata.</summary>
        public Builder WithMetadata(CloudPlaceholderMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            if (metadata.Kind is not CloudItemKind.Directory)
            {
                throw new ArgumentException("Directory placeholder metadata is required.", nameof(metadata));
            }

            _metadata = metadata;
            return this;
        }

        /// <summary>Sets whether creation marks the directory in sync.</summary>
        public Builder WithInSyncState(bool inSync)
        {
            _initiallyInSync = inSync;
            return this;
        }

        /// <summary>Sets explicit collision behavior.</summary>
        public Builder WithCollisionBehavior(CloudPlaceholderCollisionBehavior behavior)
        {
            _collisionBehavior = CloudPlaceholderConversionOptions.RequireDefined(
                behavior,
                nameof(behavior));
            return this;
        }

        /// <summary>Sets initial directory population state.</summary>
        public Builder WithPopulationState(CloudDirectoryPopulationState state)
        {
            _populationState = CloudPlaceholderConversionOptions.RequireDefined(state, nameof(state));
            return this;
        }

        /// <summary>Creates the immutable directory specification.</summary>
        public CloudDirectoryPlaceholderSpec Build() => new(
            _name,
            _identity,
            _metadata,
            _initiallyInSync,
            _collisionBehavior,
            _populationState);
    }
}
