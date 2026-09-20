#include <cfapi.h>

#include <cstddef>
#include <iostream>

int main()
{
    // The probe emits machine-readable ABI facts obtained from the active
    // Windows SDK. Managed tests will compare these values with the bindings.
    std::cout << "{\n"
              << "  \"pointerSize\": " << sizeof(void*) << ",\n"
              << "  \"cfPlatformInfoSize\": " << sizeof(CF_PLATFORM_INFO) << ",\n"
              << "  \"cfPlatformInfoBuildNumberOffset\": " << offsetof(CF_PLATFORM_INFO, BuildNumber) << ",\n"
              << "  \"cfPlatformInfoRevisionNumberOffset\": " << offsetof(CF_PLATFORM_INFO, RevisionNumber) << ",\n"
              << "  \"cfPlatformInfoIntegrationNumberOffset\": " << offsetof(CF_PLATFORM_INFO, IntegrationNumber) << ",\n"
              << "  \"cfHydrationPolicySize\": " << sizeof(CF_HYDRATION_POLICY) << ",\n"
              << "  \"cfPopulationPolicySize\": " << sizeof(CF_POPULATION_POLICY) << ",\n"
              << "  \"cfSyncPoliciesSize\": " << sizeof(CF_SYNC_POLICIES) << ",\n"
              << "  \"cfSyncPoliciesHydrationOffset\": " << offsetof(CF_SYNC_POLICIES, Hydration) << ",\n"
              << "  \"cfSyncPoliciesPopulationOffset\": " << offsetof(CF_SYNC_POLICIES, Population) << ",\n"
              << "  \"cfSyncPoliciesInSyncOffset\": " << offsetof(CF_SYNC_POLICIES, InSync) << ",\n"
              << "  \"cfSyncPoliciesHardLinkOffset\": " << offsetof(CF_SYNC_POLICIES, HardLink) << ",\n"
              << "  \"cfSyncPoliciesPlaceholderManagementOffset\": " << offsetof(CF_SYNC_POLICIES, PlaceholderManagement) << ",\n"
              << "  \"cfSyncRegistrationSize\": " << sizeof(CF_SYNC_REGISTRATION) << ",\n"
              << "  \"cfSyncRegistrationProviderNameOffset\": " << offsetof(CF_SYNC_REGISTRATION, ProviderName) << ",\n"
              << "  \"cfSyncRegistrationProviderVersionOffset\": " << offsetof(CF_SYNC_REGISTRATION, ProviderVersion) << ",\n"
              << "  \"cfSyncRegistrationSyncRootIdentityOffset\": " << offsetof(CF_SYNC_REGISTRATION, SyncRootIdentity) << ",\n"
              << "  \"cfSyncRegistrationSyncRootIdentityLengthOffset\": " << offsetof(CF_SYNC_REGISTRATION, SyncRootIdentityLength) << ",\n"
              << "  \"cfSyncRegistrationFileIdentityOffset\": " << offsetof(CF_SYNC_REGISTRATION, FileIdentity) << ",\n"
              << "  \"cfSyncRegistrationFileIdentityLengthOffset\": " << offsetof(CF_SYNC_REGISTRATION, FileIdentityLength) << ",\n"
              << "  \"cfSyncRegistrationProviderIdOffset\": " << offsetof(CF_SYNC_REGISTRATION, ProviderId) << ",\n"
              << "  \"cfSyncRootBasicInfoSize\": " << sizeof(CF_SYNC_ROOT_BASIC_INFO) << ",\n"
              << "  \"cfSyncRootProviderInfoSize\": " << sizeof(CF_SYNC_ROOT_PROVIDER_INFO) << ",\n"
              << "  \"cfSyncRootProviderInfoProviderNameOffset\": " << offsetof(CF_SYNC_ROOT_PROVIDER_INFO, ProviderName) << ",\n"
              << "  \"cfSyncRootProviderInfoProviderVersionOffset\": " << offsetof(CF_SYNC_ROOT_PROVIDER_INFO, ProviderVersion) << ",\n"
              << "  \"cfSyncRootStandardInfoSize\": " << sizeof(CF_SYNC_ROOT_STANDARD_INFO) << ",\n"
              << "  \"cfSyncRootStandardInfoProviderNameOffset\": " << offsetof(CF_SYNC_ROOT_STANDARD_INFO, ProviderName) << ",\n"
              << "  \"cfSyncRootStandardInfoProviderVersionOffset\": " << offsetof(CF_SYNC_ROOT_STANDARD_INFO, ProviderVersion) << ",\n"
              << "  \"cfSyncRootStandardInfoIdentityLengthOffset\": " << offsetof(CF_SYNC_ROOT_STANDARD_INFO, SyncRootIdentityLength) << ",\n"
              << "  \"cfSyncRootStandardInfoIdentityOffset\": " << offsetof(CF_SYNC_ROOT_STANDARD_INFO, SyncRootIdentity) << ",\n"
              << "  \"cfRegisterFlagMarkInSyncOnRoot\": " << CF_REGISTER_FLAG_MARK_IN_SYNC_ON_ROOT << ",\n"
              << "  \"cfHydrationModifierAllowFullRestart\": " << CF_HYDRATION_POLICY_MODIFIER_ALLOW_FULL_RESTART_HYDRATION << ",\n"
              << "  \"cfInSyncPolicyTrackAll\": " << CF_INSYNC_POLICY_TRACK_ALL << ",\n"
              << "  \"cfSyncRootInfoProvider\": " << CF_SYNC_ROOT_INFO_PROVIDER << ",\n"
              << "  \"cfCallbackInfoSize\": " << sizeof(CF_CALLBACK_INFO) << ",\n"
              << "  \"cfOperationInfoSize\": " << sizeof(CF_OPERATION_INFO) << ",\n"
              << "  \"cfOperationParametersSize\": " << sizeof(CF_OPERATION_PARAMETERS) << "\n"
              << "}\n";

    return 0;
}
