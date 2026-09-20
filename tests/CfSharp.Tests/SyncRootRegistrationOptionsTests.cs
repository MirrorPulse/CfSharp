namespace CfSharp.Tests;

public sealed class SyncRootRegistrationOptionsTests
{
    [Fact]
    public void BuilderCreatesConservativeDefaults()
    {
        SyncRootRegistrationOptions options =
            SyncRootRegistrationOptions.CreateBuilder("Example Cloud", "1.0.0").Build();

        Assert.Equal("Example Cloud", options.ProviderName);
        Assert.Equal("1.0.0", options.ProviderVersion);
        Assert.Equal(Guid.Empty, options.ProviderId);
        Assert.True(options.SyncRootIdentity.IsEmpty);
        Assert.True(options.FileIdentity.IsEmpty);
        Assert.Equal(CloudHydrationPolicy.Progressive, options.HydrationPolicy);
        Assert.Equal(CloudHydrationPolicyModifiers.None, options.HydrationModifiers);
        Assert.Equal(CloudPopulationPolicy.Partial, options.PopulationPolicy);
        Assert.Equal(CloudInSyncPolicy.TrackAll, options.InSyncPolicy);
        Assert.Equal(CloudHardLinkPolicy.Disallowed, options.HardLinkPolicy);
        Assert.Equal(
            CloudPlaceholderManagementPolicy.ProviderOnly,
            options.PlaceholderManagementPolicy);
        Assert.False(options.UpdateExisting);
        Assert.False(options.DisableOnDemandPopulationOnRoot);
        Assert.False(options.MarkRootInSync);
    }

    [Fact]
    public void BuiltOptionsOwnIdentityCopies()
    {
        byte[] rootIdentity = [1, 2, 3];
        byte[] fileIdentity = [4, 5, 6];
        SyncRootRegistrationOptions.Builder builder =
            SyncRootRegistrationOptions.CreateBuilder("Example Cloud", "1.0.0")
                .WithSyncRootIdentity(rootIdentity)
                .WithFileIdentity(fileIdentity);

        SyncRootRegistrationOptions options = builder.Build();
        rootIdentity[0] = 9;
        fileIdentity[0] = 9;
        _ = builder.WithSyncRootIdentity([7]);
        _ = builder.WithFileIdentity([8]);

        Assert.True(options.SyncRootIdentity.SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.True(options.FileIdentity.SequenceEqual(new byte[] { 4, 5, 6 }));
    }

    [Fact]
    public void BuilderPreservesSelectedPoliciesAndFlags()
    {
        Guid providerId = Guid.NewGuid();
        SyncRootRegistrationOptions options =
            SyncRootRegistrationOptions.CreateBuilder("Example Cloud", "2.0.0")
                .WithProviderId(providerId)
                .WithHydrationPolicy(
                    CloudHydrationPolicy.Full,
                    CloudHydrationPolicyModifiers.AutoDehydrationAllowed)
                .WithPopulationPolicy(CloudPopulationPolicy.Full)
                .WithInSyncPolicy(CloudInSyncPolicy.TrackFileAll)
                .AllowHardLinks()
                .WithPlaceholderManagement(
                    CloudPlaceholderManagementPolicy.CreateUnrestricted |
                    CloudPlaceholderManagementPolicy.UpdateUnrestricted)
                .WithExistingRegistrationUpdate()
                .WithOnDemandPopulationDisabledOnRoot()
                .WithRootMarkedInSync()
                .Build();

        Assert.Equal(providerId, options.ProviderId);
        Assert.Equal(CloudHydrationPolicy.Full, options.HydrationPolicy);
        Assert.Equal(
            CloudHydrationPolicyModifiers.AutoDehydrationAllowed,
            options.HydrationModifiers);
        Assert.Equal(CloudPopulationPolicy.Full, options.PopulationPolicy);
        Assert.Equal(CloudInSyncPolicy.TrackFileAll, options.InSyncPolicy);
        Assert.Equal(CloudHardLinkPolicy.Allowed, options.HardLinkPolicy);
        Assert.Equal(
            CloudPlaceholderManagementPolicy.CreateUnrestricted |
            CloudPlaceholderManagementPolicy.UpdateUnrestricted,
            options.PlaceholderManagementPolicy);
        Assert.True(options.UpdateExisting);
        Assert.True(options.DisableOnDemandPopulationOnRoot);
        Assert.True(options.MarkRootInSync);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuilderRejectsEmptyProviderText(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            SyncRootRegistrationOptions.CreateBuilder(value, "1.0.0"));
        Assert.Throws<ArgumentException>(() =>
            SyncRootRegistrationOptions.CreateBuilder("Example", value));
    }

    [Fact]
    public void BuilderRejectsOversizedIdentityData()
    {
        SyncRootRegistrationOptions.Builder builder =
            SyncRootRegistrationOptions.CreateBuilder("Example", "1.0.0");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.WithSyncRootIdentity(
                new byte[SyncRootRegistrationOptions.MaxSyncRootIdentityLength + 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.WithFileIdentity(
                new byte[SyncRootRegistrationOptions.MaxFileIdentityLength + 1]));
    }

    [Fact]
    public void BuildRejectsMutuallyExclusiveHydrationModifiers()
    {
        SyncRootRegistrationOptions.Builder builder =
            SyncRootRegistrationOptions.CreateBuilder("Example", "1.0.0")
                .WithHydrationPolicy(
                    CloudHydrationPolicy.Progressive,
                    CloudHydrationPolicyModifiers.ValidationRequired |
                    CloudHydrationPolicyModifiers.StreamingAllowed);

        Assert.Throws<ArgumentException>(() => builder.Build());
    }
}
