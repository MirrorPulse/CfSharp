namespace CfSharp;

/// <summary>
/// Defines immutable provider identity, policy, and root-state options for sync-root registration.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="CreateBuilder"/> to construct an instance. Identity data is copied during
/// configuration and again during <see cref="Builder.Build"/>, so later caller or builder
/// mutations cannot alter an existing options object.
/// </para>
/// <para>
/// Instances own only managed memory, may be retained indefinitely, and are safe for
/// concurrent reads. Registration itself is performed by <see cref="CloudSyncRoot.Register"/>.
/// </para>
/// </remarks>
public sealed class SyncRootRegistrationOptions
{
    /// <summary>Maximum sync-root identity length accepted by Windows, in bytes.</summary>
    public const int MaxSyncRootIdentityLength = 64 * 1024;

    /// <summary>Maximum root file-identity length accepted by Windows, in bytes.</summary>
    public const int MaxFileIdentityLength = 4 * 1024;

    /// <summary>Maximum provider name or version length, in UTF-16 characters.</summary>
    public const int MaxProviderTextLength = 255;

    private readonly byte[] _syncRootIdentity;
    private readonly byte[] _fileIdentity;

    private SyncRootRegistrationOptions(Builder builder)
    {
        ProviderName = builder.ProviderName;
        ProviderVersion = builder.ProviderVersion;
        ProviderId = builder.ProviderId;
        _syncRootIdentity = (byte[])builder.SyncRootIdentity.Clone();
        _fileIdentity = (byte[])builder.FileIdentity.Clone();
        HydrationPolicy = builder.HydrationPolicy;
        HydrationModifiers = builder.HydrationModifiers;
        PopulationPolicy = builder.PopulationPolicy;
        InSyncPolicy = builder.InSyncPolicy;
        HardLinkPolicy = builder.HardLinkPolicy;
        PlaceholderManagementPolicy = builder.PlaceholderManagementPolicy;
        UpdateExisting = builder.UpdateExisting;
        DisableOnDemandPopulationOnRoot = builder.DisableOnDemandPopulationOnRoot;
        MarkRootInSync = builder.MarkRootInSync;
    }

    /// <summary>Gets the user-facing provider name persisted by Windows.</summary>
    public string ProviderName { get; }

    /// <summary>Gets the user-facing provider version persisted by Windows.</summary>
    public string ProviderVersion { get; }

    /// <summary>
    /// Gets the stable provider identifier used by Windows for telemetry correlation.
    /// </summary>
    /// <remarks>
    /// A value of <see cref="Guid.Empty"/> asks Windows to derive an identifier from
    /// <see cref="ProviderName"/>. A stable explicit identifier is recommended.
    /// </remarks>
    public Guid ProviderId { get; }

    /// <summary>Gets a read-only view of the provider-defined sync-root identity.</summary>
    public ReadOnlySpan<byte> SyncRootIdentity => _syncRootIdentity;

    /// <summary>Gets a read-only view of the optional root placeholder file identity.</summary>
    public ReadOnlySpan<byte> FileIdentity => _fileIdentity;

    /// <summary>Gets the primary file-content hydration policy.</summary>
    public CloudHydrationPolicy HydrationPolicy { get; }

    /// <summary>Gets optional hydration behavior.</summary>
    public CloudHydrationPolicyModifiers HydrationModifiers { get; }

    /// <summary>Gets the directory namespace population policy.</summary>
    public CloudPopulationPolicy PopulationPolicy { get; }

    /// <summary>Gets the metadata changes tracked for in-sync state.</summary>
    public CloudInSyncPolicy InSyncPolicy { get; }

    /// <summary>Gets the placeholder hard-link policy.</summary>
    public CloudHardLinkPolicy HardLinkPolicy { get; }

    /// <summary>Gets placeholder-management permissions for non-provider processes.</summary>
    public CloudPlaceholderManagementPolicy PlaceholderManagementPolicy { get; }

    /// <summary>Gets whether registration updates an existing sync root.</summary>
    public bool UpdateExisting { get; }

    /// <summary>
    /// Gets whether on-demand population is disabled for the root directory itself.
    /// </summary>
    public bool DisableOnDemandPopulationOnRoot { get; }

    /// <summary>Gets whether Windows marks the root directory in sync during registration.</summary>
    public bool MarkRootInSync { get; }

    /// <summary>Creates a fluent builder with provider display information.</summary>
    /// <param name="providerName">User-facing provider name, from 1 through 255 characters.</param>
    /// <param name="providerVersion">User-facing provider version, from 1 through 255 characters.</param>
    /// <returns>A mutable builder initialized with conservative default policies.</returns>
    /// <exception cref="ArgumentException">
    /// Either value is empty, contains only white space, or exceeds 255 characters.
    /// </exception>
    public static Builder CreateBuilder(string providerName, string providerVersion) =>
        new(providerName, providerVersion);

    /// <summary>
    /// Builds an immutable <see cref="SyncRootRegistrationOptions"/> value.
    /// </summary>
    /// <remarks>
    /// A builder is mutable and not thread-safe. After <see cref="Build"/> returns, it may be
    /// reused without changing previously built values.
    /// </remarks>
    public sealed class Builder
    {
        internal Builder(string providerName, string providerVersion)
        {
            ProviderName = ValidateProviderText(providerName, nameof(providerName));
            ProviderVersion = ValidateProviderText(providerVersion, nameof(providerVersion));
        }

        internal string ProviderName { get; }

        internal string ProviderVersion { get; }

        internal Guid ProviderId { get; private set; }

        internal byte[] SyncRootIdentity { get; private set; } = [];

        internal byte[] FileIdentity { get; private set; } = [];

        internal CloudHydrationPolicy HydrationPolicy { get; private set; } =
            CloudHydrationPolicy.Progressive;

        internal CloudHydrationPolicyModifiers HydrationModifiers { get; private set; } =
            CloudHydrationPolicyModifiers.None;

        internal CloudPopulationPolicy PopulationPolicy { get; private set; } =
            CloudPopulationPolicy.Partial;

        internal CloudInSyncPolicy InSyncPolicy { get; private set; } = CloudInSyncPolicy.TrackAll;

        internal CloudHardLinkPolicy HardLinkPolicy { get; private set; } =
            CloudHardLinkPolicy.Disallowed;

        internal CloudPlaceholderManagementPolicy PlaceholderManagementPolicy { get; private set; } =
            CloudPlaceholderManagementPolicy.ProviderOnly;

        internal bool UpdateExisting { get; private set; }

        internal bool DisableOnDemandPopulationOnRoot { get; private set; }

        internal bool MarkRootInSync { get; private set; }

        /// <summary>Sets the stable provider identifier used for telemetry correlation.</summary>
        /// <param name="providerId">
        /// Stable identifier shared by registrations belonging to the same provider product.
        /// </param>
        /// <returns>This builder.</returns>
        public Builder WithProviderId(Guid providerId)
        {
            ProviderId = providerId;
            return this;
        }

        /// <summary>Sets provider-defined identity data returned with sync-root callbacks.</summary>
        /// <param name="identity">Identity bytes, with a maximum length of 64 KiB.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The identity exceeds 64 KiB.</exception>
        public Builder WithSyncRootIdentity(ReadOnlySpan<byte> identity)
        {
            if (identity.Length > MaxSyncRootIdentityLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(identity),
                    identity.Length,
                    $"Sync-root identity cannot exceed {MaxSyncRootIdentityLength} bytes.");
            }

            SyncRootIdentity = identity.ToArray();
            return this;
        }

        /// <summary>Sets optional file identity data for the root placeholder.</summary>
        /// <param name="identity">Identity bytes, with a maximum length of 4 KiB.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The identity exceeds 4 KiB.</exception>
        public Builder WithFileIdentity(ReadOnlySpan<byte> identity)
        {
            if (identity.Length > MaxFileIdentityLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(identity),
                    identity.Length,
                    $"File identity cannot exceed {MaxFileIdentityLength} bytes.");
            }

            FileIdentity = identity.ToArray();
            return this;
        }

        /// <summary>Sets the primary hydration policy and optional modifiers.</summary>
        /// <param name="policy">Primary content hydration behavior.</param>
        /// <param name="modifiers">Additional validation, streaming, or dehydration behavior.</param>
        /// <returns>This builder.</returns>
        public Builder WithHydrationPolicy(
            CloudHydrationPolicy policy,
            CloudHydrationPolicyModifiers modifiers = CloudHydrationPolicyModifiers.None)
        {
            HydrationPolicy = policy;
            HydrationModifiers = modifiers;
            return this;
        }

        /// <summary>Sets the namespace population policy.</summary>
        /// <param name="policy">Directory population behavior.</param>
        /// <returns>This builder.</returns>
        public Builder WithPopulationPolicy(CloudPopulationPolicy policy)
        {
            PopulationPolicy = policy;
            return this;
        }

        /// <summary>Sets the metadata changes tracked for in-sync state.</summary>
        /// <param name="policy">Metadata tracking flags.</param>
        /// <returns>This builder.</returns>
        public Builder WithInSyncPolicy(CloudInSyncPolicy policy)
        {
            InSyncPolicy = policy;
            return this;
        }

        /// <summary>Enables or disables hard links for placeholders.</summary>
        /// <param name="allow"><see langword="true"/> to allow hard links.</param>
        /// <returns>This builder.</returns>
        public Builder AllowHardLinks(bool allow = true)
        {
            HardLinkPolicy = allow ? CloudHardLinkPolicy.Allowed : CloudHardLinkPolicy.Disallowed;
            return this;
        }

        /// <summary>Sets placeholder-management permissions for non-provider processes.</summary>
        /// <param name="policy">Placeholder-management permission flags.</param>
        /// <returns>This builder.</returns>
        public Builder WithPlaceholderManagement(CloudPlaceholderManagementPolicy policy)
        {
            PlaceholderManagementPolicy = policy;
            return this;
        }

        /// <summary>Controls whether an existing registration is updated.</summary>
        /// <param name="enabled"><see langword="true"/> to request update behavior.</param>
        /// <returns>This builder.</returns>
        public Builder WithExistingRegistrationUpdate(bool enabled = true)
        {
            UpdateExisting = enabled;
            return this;
        }

        /// <summary>Controls on-demand population for the root directory itself.</summary>
        /// <param name="disabled">
        /// <see langword="true"/> to disable on-demand population only on the root.
        /// </param>
        /// <returns>This builder.</returns>
        public Builder WithOnDemandPopulationDisabledOnRoot(bool disabled = true)
        {
            DisableOnDemandPopulationOnRoot = disabled;
            return this;
        }

        /// <summary>Controls whether registration marks the root directory in sync.</summary>
        /// <param name="markInSync"><see langword="true"/> to mark the root in sync.</param>
        /// <returns>This builder.</returns>
        public Builder WithRootMarkedInSync(bool markInSync = true)
        {
            MarkRootInSync = markInSync;
            return this;
        }

        /// <summary>Validates the selected policies and creates an immutable options object.</summary>
        /// <returns>An independent immutable registration configuration.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A policy contains an undefined value.</exception>
        /// <exception cref="ArgumentException">
        /// Validation-required and streaming-allowed hydration modifiers are both selected.
        /// </exception>
        public SyncRootRegistrationOptions Build()
        {
            ValidatePolicies();
            return new SyncRootRegistrationOptions(this);
        }

        private void ValidatePolicies()
        {
            if (!Enum.IsDefined(HydrationPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(HydrationPolicy), HydrationPolicy, null);
            }

            const CloudHydrationPolicyModifiers validHydrationModifiers =
                CloudHydrationPolicyModifiers.ValidationRequired |
                CloudHydrationPolicyModifiers.StreamingAllowed |
                CloudHydrationPolicyModifiers.AutoDehydrationAllowed |
                CloudHydrationPolicyModifiers.AllowFullRestartHydration;
            if ((HydrationModifiers & ~validHydrationModifiers) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(HydrationModifiers),
                    HydrationModifiers,
                    null);
            }

            if (HydrationModifiers.HasFlag(CloudHydrationPolicyModifiers.ValidationRequired) &&
                HydrationModifiers.HasFlag(CloudHydrationPolicyModifiers.StreamingAllowed))
            {
                throw new ArgumentException(
                    "Validation-required and streaming-allowed hydration cannot be combined.",
                    nameof(HydrationModifiers));
            }

            if (!Enum.IsDefined(PopulationPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(PopulationPolicy), PopulationPolicy, null);
            }

            const CloudInSyncPolicy validInSyncPolicy =
                CloudInSyncPolicy.TrackAll | CloudInSyncPolicy.PreserveForSyncEngine;
            if ((InSyncPolicy & ~validInSyncPolicy) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(InSyncPolicy), InSyncPolicy, null);
            }

            if (!Enum.IsDefined(HardLinkPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(HardLinkPolicy), HardLinkPolicy, null);
            }

            const CloudPlaceholderManagementPolicy validManagementPolicy =
                CloudPlaceholderManagementPolicy.CreateUnrestricted |
                CloudPlaceholderManagementPolicy.ConvertUnrestricted |
                CloudPlaceholderManagementPolicy.UpdateUnrestricted;
            if ((PlaceholderManagementPolicy & ~validManagementPolicy) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(PlaceholderManagementPolicy),
                    PlaceholderManagementPolicy,
                    null);
            }
        }
    }

    private static string ValidateProviderText(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be empty or white space.", parameterName);
        }

        if (value.Length > MaxProviderTextLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Length,
                $"The value cannot exceed {MaxProviderTextLength} UTF-16 characters.");
        }

        return value;
    }
}
