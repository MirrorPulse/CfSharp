using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CfSharp.Native.Tests;

public sealed class SyncRootLayoutTests
{
    [Fact]
    public void PolicyLayoutsMatchWindowsSdk()
    {
        Assert.Equal(2, Unsafe.SizeOf<CfHydrationPolicyPrimary>());
        Assert.Equal(2, Unsafe.SizeOf<CfHydrationPolicyModifier>());
        Assert.Equal(2, Unsafe.SizeOf<CfPopulationPolicyPrimary>());
        Assert.Equal(2, Unsafe.SizeOf<CfPopulationPolicyModifier>());

        AssertLayout<CfHydrationPolicy>(4,
            (nameof(CfHydrationPolicy.Primary), 0),
            (nameof(CfHydrationPolicy.Modifier), 2));
        AssertLayout<CfPopulationPolicy>(4,
            (nameof(CfPopulationPolicy.Primary), 0),
            (nameof(CfPopulationPolicy.Modifier), 2));
        AssertLayout<CfSyncPolicies>(24,
            (nameof(CfSyncPolicies.StructSize), 0),
            (nameof(CfSyncPolicies.Hydration), 4),
            (nameof(CfSyncPolicies.Population), 8),
            (nameof(CfSyncPolicies.InSync), 12),
            (nameof(CfSyncPolicies.HardLink), 16),
            (nameof(CfSyncPolicies.PlaceholderManagement), 20));
    }

    [Fact]
    public void RegistrationLayoutMatchesWindowsSdkForCurrentArchitecture()
    {
        int expectedSize = IntPtr.Size == 8 ? 72 : 44;
        int[] expectedOffsets = IntPtr.Size == 8
            ? [0, 8, 16, 24, 32, 40, 48, 52]
            : [0, 4, 8, 12, 16, 20, 24, 28];

        AssertLayout<CfSyncRegistration>(expectedSize,
            (nameof(CfSyncRegistration.StructSize), expectedOffsets[0]),
            (nameof(CfSyncRegistration.ProviderName), expectedOffsets[1]),
            (nameof(CfSyncRegistration.ProviderVersion), expectedOffsets[2]),
            (nameof(CfSyncRegistration.SyncRootIdentity), expectedOffsets[3]),
            (nameof(CfSyncRegistration.SyncRootIdentityLength), expectedOffsets[4]),
            (nameof(CfSyncRegistration.FileIdentity), expectedOffsets[5]),
            (nameof(CfSyncRegistration.FileIdentityLength), expectedOffsets[6]),
            (nameof(CfSyncRegistration.ProviderId), expectedOffsets[7]));
    }

    [Fact]
    public void QueryResultLayoutsMatchWindowsSdk()
    {
        AssertLayout<CfSyncRootBasicInfo>(8,
            (nameof(CfSyncRootBasicInfo.SyncRootFileId), 0));
        AssertLayout<CfSyncRootProviderInfo>(1028,
            (nameof(CfSyncRootProviderInfo.ProviderStatus), 0),
            (nameof(CfSyncRootProviderInfo.ProviderName), 4),
            (nameof(CfSyncRootProviderInfo.ProviderVersion), 516));
        AssertLayout<CfSyncRootStandardInfo>(1064,
            (nameof(CfSyncRootStandardInfo.SyncRootFileId), 0),
            (nameof(CfSyncRootStandardInfo.HydrationPolicy), 8),
            (nameof(CfSyncRootStandardInfo.PopulationPolicy), 12),
            (nameof(CfSyncRootStandardInfo.InSyncPolicy), 16),
            (nameof(CfSyncRootStandardInfo.HardLinkPolicy), 20),
            (nameof(CfSyncRootStandardInfo.ProviderStatus), 24),
            (nameof(CfSyncRootStandardInfo.ProviderName), 28),
            (nameof(CfSyncRootStandardInfo.ProviderVersion), 540),
            (nameof(CfSyncRootStandardInfo.SyncRootIdentityLength), 1052),
            (nameof(CfSyncRootStandardInfo.SyncRootIdentity), 1056));
    }

    [Fact]
    public void ConstantsMatchWindowsSdk()
    {
        Assert.Equal(255, CfApi.MaxProviderNameLength);
        Assert.Equal(255, CfApi.MaxProviderVersionLength);
        Assert.Equal(4096, CfApi.MaxFileIdentityLength);

        Assert.Equal(0x00000004u, (uint)CfRegisterFlags.MarkInSyncOnRoot);
        Assert.Equal(0x0008, (ushort)CfHydrationPolicyModifier.AllowFullRestartHydration);
        Assert.Equal(0x0055550fu, (uint)CfInSyncPolicy.TrackFileAll);
        Assert.Equal(0x00aaaaf0u, (uint)CfInSyncPolicy.TrackDirectoryAll);
        Assert.Equal(0x80000000u, (uint)CfInSyncPolicy.PreserveInSyncForSyncEngine);
        Assert.Equal(0xC0000002u, (uint)CfSyncProviderStatus.Error);
        Assert.Equal(2, (int)CfSyncRootInfoClass.Provider);
    }

    private static void AssertLayout<T>(
        int expectedSize,
        params (string FieldName, int ExpectedOffset)[] fields)
        where T : struct
    {
        Assert.Equal(expectedSize, Marshal.SizeOf<T>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<T>());

        foreach ((string fieldName, int expectedOffset) in fields)
        {
            Assert.Equal(expectedOffset, Marshal.OffsetOf<T>(fieldName).ToInt32());
        }
    }
}
