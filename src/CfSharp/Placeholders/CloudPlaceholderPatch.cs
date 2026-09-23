namespace CfSharp;

/// <summary>Describes an explicit immutable patch for an existing placeholder.</summary>
/// <remarks>
/// Omitted values remain unchanged. Identity removal, property removal, and state changes are
/// explicit so null never ambiguously means both remove and preserve. Range data is copied during
/// construction and is safe for concurrent reads.
/// </remarks>
public sealed class CloudPlaceholderPatch
{
    private readonly IReadOnlyList<CloudFileRange> _dehydrateRanges;

    private CloudPlaceholderPatch(Builder builder)
    {
        Metadata = builder.Metadata;
        MetadataWriteMode = builder.MetadataWriteMode;
        IdentityChange = builder.IdentityChange;
        ReplacementIdentity = builder.ReplacementIdentity;
        SynchronizationChange = builder.SynchronizationChange;
        PopulationState = builder.PopulationState;
        ContentMode = builder.ContentMode;
        DehydrateWholeFile = builder.DehydrateWholeFile;
        _dehydrateRanges = Array.AsReadOnly(builder.DehydrateRanges.ToArray());
        RemoveExtrinsicProperties = builder.RemoveExtrinsicProperties;
        RequireInSync = builder.RequireInSync;
        FileSize = builder.FileSize;
        ExpectedUsn = builder.ExpectedUsn;
    }

    /// <summary>Gets replacement metadata, or null when metadata remains unchanged.</summary>
    public CloudPlaceholderMetadata? Metadata { get; }

    /// <summary>Gets how zero-valued metadata fields are written.</summary>
    public CloudMetadataWriteMode MetadataWriteMode { get; }

    /// <summary>Gets whether identity is unchanged, replaced, or removed.</summary>
    public CloudPlaceholderIdentityChange IdentityChange { get; }

    /// <summary>Gets replacement identity when <see cref="IdentityChange"/> is Replace.</summary>
    public CloudPlaceholderIdentity? ReplacementIdentity { get; }

    /// <summary>Gets an explicit in-sync state change.</summary>
    public CloudPlaceholderSynchronizationChange SynchronizationChange { get; }

    /// <summary>Gets an optional directory population-state change.</summary>
    public CloudDirectoryPopulationState? PopulationState { get; }

    /// <summary>Gets an optional file content-mode change.</summary>
    public CloudFileContentMode? ContentMode { get; }

    /// <summary>Gets whether the complete file is dehydrated atomically with the update.</summary>
    public bool DehydrateWholeFile { get; }

    /// <summary>Gets copied ranges to dehydrate atomically with the update.</summary>
    public IReadOnlyList<CloudFileRange> DehydrateRanges => _dehydrateRanges;

    /// <summary>Gets whether all extrinsic placeholder properties are removed.</summary>
    public bool RemoveExtrinsicProperties { get; }

    /// <summary>Gets whether the update fails unless the placeholder is currently in sync.</summary>
    public bool RequireInSync { get; }

    /// <summary>
    /// Gets the replacement logical file length, or null when the current length is preserved.
    /// </summary>
    /// <remarks>The value is valid only when replacement metadata is also supplied.</remarks>
    public long? FileSize { get; }

    /// <summary>Gets the expected current USN, or null for no condition.</summary>
    public long? ExpectedUsn { get; }

    /// <summary>Creates a mutable patch builder.</summary>
    public static Builder CreateBuilder() => new();

    /// <summary>Builds immutable placeholder patches.</summary>
    public sealed class Builder
    {
        internal CloudPlaceholderMetadata? Metadata { get; private set; }

        internal CloudMetadataWriteMode MetadataWriteMode { get; private set; }

        internal CloudPlaceholderIdentityChange IdentityChange { get; private set; }

        internal CloudPlaceholderIdentity? ReplacementIdentity { get; private set; }

        internal CloudPlaceholderSynchronizationChange SynchronizationChange { get; private set; }

        internal CloudDirectoryPopulationState? PopulationState { get; private set; }

        internal CloudFileContentMode? ContentMode { get; private set; }

        internal bool DehydrateWholeFile { get; private set; }

        internal List<CloudFileRange> DehydrateRanges { get; } = [];

        internal bool RemoveExtrinsicProperties { get; private set; }

        internal bool RequireInSync { get; private set; }

        internal long? FileSize { get; private set; }

        internal long? ExpectedUsn { get; private set; }

        /// <summary>Replaces file-system metadata using the selected zero-value behavior.</summary>
        public Builder WithMetadata(
            CloudPlaceholderMetadata metadata,
            CloudMetadataWriteMode writeMode = CloudMetadataWriteMode.PreserveUnspecified)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            Metadata = metadata;
            MetadataWriteMode = CloudPlaceholderConversionOptions.RequireDefined(
                writeMode,
                nameof(writeMode));
            return this;
        }

        /// <summary>Replaces the complete CfSharp placeholder identity.</summary>
        public Builder WithIdentity(CloudPlaceholderIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(identity);
            IdentityChange = CloudPlaceholderIdentityChange.Replace;
            ReplacementIdentity = identity;
            return this;
        }

        /// <summary>Removes native placeholder identity data.</summary>
        public Builder WithIdentityRemoval()
        {
            IdentityChange = CloudPlaceholderIdentityChange.Remove;
            ReplacementIdentity = null;
            return this;
        }

        /// <summary>Sets whether the updated placeholder is marked in sync.</summary>
        public Builder WithInSyncState(bool inSync)
        {
            SynchronizationChange = inSync
                ? CloudPlaceholderSynchronizationChange.MarkInSync
                : CloudPlaceholderSynchronizationChange.MarkNotInSync;
            return this;
        }

        /// <summary>Sets directory population state.</summary>
        public Builder WithPopulationState(CloudDirectoryPopulationState state)
        {
            PopulationState = CloudPlaceholderConversionOptions.RequireDefined(state, nameof(state));
            return this;
        }

        /// <summary>Sets file content mode.</summary>
        public Builder WithContentMode(CloudFileContentMode mode)
        {
            ContentMode = CloudPlaceholderConversionOptions.RequireDefined(mode, nameof(mode));
            return this;
        }

        /// <summary>Requests full-file dehydration as part of the update.</summary>
        public Builder WithFullDehydration()
        {
            DehydrateWholeFile = true;
            return this;
        }

        /// <summary>Replaces the set of ranges dehydrated atomically with the update.</summary>
        public Builder WithDehydratedRanges(IEnumerable<CloudFileRange> ranges)
        {
            ArgumentNullException.ThrowIfNull(ranges);
            DehydrateRanges.Clear();
            foreach (CloudFileRange range in ranges)
            {
                range.Validate(nameof(ranges));
                DehydrateRanges.Add(range);
            }

            return this;
        }

        /// <summary>Requests removal of all extrinsic placeholder properties.</summary>
        public Builder WithExtrinsicPropertyRemoval()
        {
            RemoveExtrinsicProperties = true;
            return this;
        }

        /// <summary>Requires the placeholder to be in sync when the atomic update begins.</summary>
        public Builder WithInSyncVerification()
        {
            RequireInSync = true;
            return this;
        }

        /// <summary>Replaces the logical file length written with replacement metadata.</summary>
        /// <param name="length">Non-negative logical length of the file.</param>
        public Builder WithFileSize(long length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            FileSize = length;
            return this;
        }

        /// <summary>Makes the update conditional on a positive current USN.</summary>
        public Builder WithExpectedUsn(long expectedUsn)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedUsn);
            ExpectedUsn = expectedUsn;
            return this;
        }

        /// <summary>Creates the immutable validated patch.</summary>
        /// <exception cref="InvalidOperationException">
        /// No change was requested or mutually exclusive changes were combined.
        /// </exception>
        public CloudPlaceholderPatch Build()
        {
            if (DehydrateWholeFile && DehydrateRanges.Count != 0)
            {
                throw new InvalidOperationException(
                    "Full dehydration and explicit dehydration ranges are mutually exclusive.");
            }

            if (ContentMode is CloudFileContentMode.AlwaysFull &&
                (DehydrateWholeFile || DehydrateRanges.Count != 0))
            {
                throw new InvalidOperationException(
                    "An always-full placeholder cannot be dehydrated by the same patch.");
            }

            if (FileSize is not null && Metadata is null)
            {
                throw new InvalidOperationException(
                    "A replacement file size requires replacement metadata.");
            }

            bool hasChange = Metadata is not null ||
                IdentityChange is not CloudPlaceholderIdentityChange.Unchanged ||
                SynchronizationChange is not CloudPlaceholderSynchronizationChange.Unchanged ||
                PopulationState is not null ||
                ContentMode is not null ||
                DehydrateWholeFile ||
                DehydrateRanges.Count != 0 ||
                RemoveExtrinsicProperties ||
                RequireInSync ||
                FileSize is not null;
            if (!hasChange)
            {
                throw new InvalidOperationException("A placeholder patch must request at least one change.");
            }

            return new CloudPlaceholderPatch(this);
        }
    }
}

/// <summary>Describes an explicit native placeholder-identity change.</summary>
public enum CloudPlaceholderIdentityChange
{
    /// <summary>Preserve existing identity data.</summary>
    Unchanged = 0,

    /// <summary>Replace identity with <see cref="CloudPlaceholderPatch.ReplacementIdentity"/>.</summary>
    Replace = 1,

    /// <summary>Remove existing identity data.</summary>
    Remove = 2,
}

/// <summary>Describes an explicit placeholder synchronization-state change.</summary>
public enum CloudPlaceholderSynchronizationChange
{
    /// <summary>Preserve current synchronization state.</summary>
    Unchanged = 0,

    /// <summary>Mark the placeholder in sync.</summary>
    MarkInSync = 1,

    /// <summary>Mark the placeholder not in sync.</summary>
    MarkNotInSync = 2,
}
