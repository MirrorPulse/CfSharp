using System.Runtime.InteropServices;
using System.Text.Json;

namespace CfSharp.Native.Tests;

public sealed class AbiProbeComparisonTests
{
    private const string ProbePathEnvironmentVariable = "CFSHARP_ABI_PROBE_JSON";

    [Fact]
    public void ManagedLayoutsAndConstantsMatchNativeProbeWhenProvided()
    {
        string? probePath = Environment.GetEnvironmentVariable(ProbePathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(probePath))
        {
            return;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(probePath));
        JsonElement probe = document.RootElement;

        AssertProbe(probe, "pointerSize", IntPtr.Size);
        AssertProbe(probe, "cfPlatformInfoSize", Marshal.SizeOf<CfPlatformInfo>());
        AssertOffset<CfPlatformInfo>(probe, "cfPlatformInfoBuildNumberOffset", nameof(CfPlatformInfo.BuildNumber));
        AssertOffset<CfPlatformInfo>(probe, "cfPlatformInfoRevisionNumberOffset", nameof(CfPlatformInfo.RevisionNumber));
        AssertOffset<CfPlatformInfo>(probe, "cfPlatformInfoIntegrationNumberOffset", nameof(CfPlatformInfo.IntegrationNumber));
        AssertProbe(probe, "cfHydrationPolicySize", Marshal.SizeOf<CfHydrationPolicy>());
        AssertProbe(probe, "cfPopulationPolicySize", Marshal.SizeOf<CfPopulationPolicy>());
        AssertProbe(probe, "cfSyncPoliciesSize", Marshal.SizeOf<CfSyncPolicies>());
        AssertOffset<CfSyncPolicies>(probe, "cfSyncPoliciesHydrationOffset", nameof(CfSyncPolicies.Hydration));
        AssertOffset<CfSyncPolicies>(probe, "cfSyncPoliciesPopulationOffset", nameof(CfSyncPolicies.Population));
        AssertOffset<CfSyncPolicies>(probe, "cfSyncPoliciesInSyncOffset", nameof(CfSyncPolicies.InSync));
        AssertOffset<CfSyncPolicies>(probe, "cfSyncPoliciesHardLinkOffset", nameof(CfSyncPolicies.HardLink));
        AssertOffset<CfSyncPolicies>(probe, "cfSyncPoliciesPlaceholderManagementOffset", nameof(CfSyncPolicies.PlaceholderManagement));
        AssertProbe(probe, "cfSyncRegistrationSize", Marshal.SizeOf<CfSyncRegistration>());
        AssertOffset<CfSyncRegistration>(probe, "cfSyncRegistrationProviderNameOffset", nameof(CfSyncRegistration.ProviderName));
        AssertOffset<CfSyncRegistration>(probe, "cfSyncRegistrationProviderVersionOffset", nameof(CfSyncRegistration.ProviderVersion));
        AssertOffset<CfSyncRegistration>(probe, "cfSyncRegistrationSyncRootIdentityOffset", nameof(CfSyncRegistration.SyncRootIdentity));
        AssertOffset<CfSyncRegistration>(probe, "cfSyncRegistrationSyncRootIdentityLengthOffset", nameof(CfSyncRegistration.SyncRootIdentityLength));
        AssertOffset<CfSyncRegistration>(probe, "cfSyncRegistrationFileIdentityOffset", nameof(CfSyncRegistration.FileIdentity));
        AssertOffset<CfSyncRegistration>(probe, "cfSyncRegistrationFileIdentityLengthOffset", nameof(CfSyncRegistration.FileIdentityLength));
        AssertOffset<CfSyncRegistration>(probe, "cfSyncRegistrationProviderIdOffset", nameof(CfSyncRegistration.ProviderId));
        AssertProbe(probe, "cfSyncRootBasicInfoSize", Marshal.SizeOf<CfSyncRootBasicInfo>());
        AssertProbe(probe, "cfSyncRootProviderInfoSize", Marshal.SizeOf<CfSyncRootProviderInfo>());
        AssertOffset<CfSyncRootProviderInfo>(probe, "cfSyncRootProviderInfoProviderNameOffset", nameof(CfSyncRootProviderInfo.ProviderName));
        AssertOffset<CfSyncRootProviderInfo>(probe, "cfSyncRootProviderInfoProviderVersionOffset", nameof(CfSyncRootProviderInfo.ProviderVersion));
        AssertProbe(probe, "cfSyncRootStandardInfoSize", Marshal.SizeOf<CfSyncRootStandardInfo>());
        AssertOffset<CfSyncRootStandardInfo>(probe, "cfSyncRootStandardInfoProviderNameOffset", nameof(CfSyncRootStandardInfo.ProviderName));
        AssertOffset<CfSyncRootStandardInfo>(probe, "cfSyncRootStandardInfoProviderVersionOffset", nameof(CfSyncRootStandardInfo.ProviderVersion));
        AssertOffset<CfSyncRootStandardInfo>(probe, "cfSyncRootStandardInfoIdentityLengthOffset", nameof(CfSyncRootStandardInfo.SyncRootIdentityLength));
        AssertOffset<CfSyncRootStandardInfo>(probe, "cfSyncRootStandardInfoIdentityOffset", nameof(CfSyncRootStandardInfo.SyncRootIdentity));
        AssertProbe(probe, "cfRegisterFlagMarkInSyncOnRoot", (uint)CfRegisterFlags.MarkInSyncOnRoot);
        AssertProbe(probe, "cfHydrationModifierAllowFullRestart", (ushort)CfHydrationPolicyModifier.AllowFullRestartHydration);
        AssertProbe(probe, "cfInSyncPolicyTrackAll", (uint)CfInSyncPolicy.TrackAll);
        AssertProbe(probe, "cfSyncRootInfoProvider", (int)CfSyncRootInfoClass.Provider);
        AssertProbe(probe, "cfConnectionKeySize", Marshal.SizeOf<CfConnectionKey>());
        AssertProbe(probe, "cfProcessInfoSize", Marshal.SizeOf<CfProcessInfo>());
        AssertProbe(probe, "cfCallbackInfoSize", Marshal.SizeOf<CfCallbackInfo>());
        AssertOffset<CfCallbackInfo>(probe, "cfCallbackInfoConnectionKeyOffset", nameof(CfCallbackInfo.ConnectionKey));
        AssertOffset<CfCallbackInfo>(probe, "cfCallbackInfoCallbackContextOffset", nameof(CfCallbackInfo.CallbackContext));
        AssertOffset<CfCallbackInfo>(probe, "cfCallbackInfoNormalizedPathOffset", nameof(CfCallbackInfo.NormalizedPath));
        AssertOffset<CfCallbackInfo>(probe, "cfCallbackInfoTransferKeyOffset", nameof(CfCallbackInfo.TransferKey));
        AssertOffset<CfCallbackInfo>(probe, "cfCallbackInfoPriorityHintOffset", nameof(CfCallbackInfo.PriorityHint));
        AssertOffset<CfCallbackInfo>(probe, "cfCallbackInfoRequestKeyOffset", nameof(CfCallbackInfo.RequestKey));
        AssertProbe(probe, "cfCallbackParametersSize", Marshal.SizeOf<CfCallbackParameters>());
        AssertOffset<CfCallbackParameters>(probe, "cfCallbackParametersUnionOffset", nameof(CfCallbackParameters.FetchData));
        AssertProbe(probe, "cfCallbackFetchDataSize", Marshal.SizeOf<CfCallbackFetchDataParameters>());
        AssertProbe(probe, "cfCallbackRegistrationSize", Marshal.SizeOf<CfCallbackRegistration>());
        AssertOffset<CfCallbackRegistration>(probe, "cfCallbackRegistrationCallbackOffset", nameof(CfCallbackRegistration.Callback));
        AssertProbe(probe, "cfCallbackTypeNone", (int)CfCallbackType.None);
        AssertProbe(probe, "cfConnectFlagBlockSelfImplicitHydration", (uint)CfConnectFlags.BlockSelfImplicitHydration);
        AssertProbe(probe, "cfOperationInfoSize", Marshal.SizeOf<CfOperationInfo>());
        AssertOffset<CfOperationInfo>(probe, "cfOperationInfoTransferKeyOffset", nameof(CfOperationInfo.TransferKey));
        AssertOffset<CfOperationInfo>(probe, "cfOperationInfoRequestKeyOffset", nameof(CfOperationInfo.RequestKey));
        AssertProbe(probe, "cfOperationParametersSize", Marshal.SizeOf<CfOperationParameters>());
        AssertOffset<CfOperationParameters>(probe, "cfOperationParametersUnionOffset", nameof(CfOperationParameters.TransferData));
        AssertProbe(probe, "cfOperationTransferDataSize", Marshal.SizeOf<CfOperationTransferDataParameters>());
        AssertProbe(probe, "cfFsMetadataSize", Marshal.SizeOf<CfFsMetadata>());
        AssertProbe(probe, "cfPlaceholderCreateInfoSize", Marshal.SizeOf<CfPlaceholderCreateInfo>());
        AssertOffset<CfPlaceholderCreateInfo>(probe, "cfPlaceholderCreateInfoMetadataOffset", nameof(CfPlaceholderCreateInfo.FsMetadata));
        AssertOffset<CfPlaceholderCreateInfo>(probe, "cfPlaceholderCreateInfoCreateUsnOffset", nameof(CfPlaceholderCreateInfo.CreateUsn));
        AssertProbe(probe, "cfPlaceholderCreateFlagAlwaysFull", (uint)CfPlaceholderCreateFlags.AlwaysFull);
        AssertProbe(probe, "cfFileRangeSize", Marshal.SizeOf<CfFileRange>());
        AssertOffset<CfFileRange>(probe, "cfFileRangeStartingOffsetOffset", nameof(CfFileRange.StartingOffset));
        AssertOffset<CfFileRange>(probe, "cfFileRangeLengthOffset", nameof(CfFileRange.Length));
        AssertProbe(probe, "cfConvertFlagForceConvertToCloudFile", (uint)CfConvertFlags.ForceConvertToCloudFile);
        AssertProbe(probe, "cfUpdateFlagAllowPartial", (uint)CfUpdateFlags.AllowPartial);
        AssertProbe(probe, "cfDehydrateFlagBackground", (uint)CfDehydrateFlags.Background);
        AssertProbe(probe, "cfPinStateInherit", (int)CfPinState.Inherit);
        AssertProbe(probe, "cfSetPinFlagRecurseStopOnError", (uint)CfSetPinFlags.RecurseStopOnError);
        AssertProbe(probe, "cfInSyncStateInSync", (int)CfInSyncState.InSync);
    }

    private static void AssertOffset<T>(
        JsonElement probe,
        string propertyName,
        string fieldName)
        where T : struct =>
        AssertProbe(probe, propertyName, Marshal.OffsetOf<T>(fieldName).ToInt64());

    private static void AssertProbe(JsonElement probe, string propertyName, long managedValue)
    {
        Assert.True(probe.TryGetProperty(propertyName, out JsonElement nativeValue), propertyName);
        Assert.Equal(managedValue, nativeValue.GetInt64());
    }
}
