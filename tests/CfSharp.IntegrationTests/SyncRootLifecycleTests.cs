using System.Runtime.Versioning;

using CfSharp.Native;
using Microsoft.Win32.SafeHandles;

namespace CfSharp.IntegrationTests;

public sealed class SyncRootLifecycleTests
{
    [Fact]
    public unsafe void RegisterQueryAndUnregisterLeavesNoRegistration()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"CfSharp-{Guid.NewGuid():N}");
        Guid registrationId = Guid.NewGuid();
        string providerName = $"CfSharp Integration {registrationId:N}";
        const string providerVersion = "1.0.0-test";
        byte[] syncRootIdentity = registrationId.ToByteArray();
        bool registered = false;

        Directory.CreateDirectory(rootPath);
        string handleQueryPath = Path.Combine(rootPath, "handle-query.txt");
        File.WriteAllText(handleQueryPath, "CfSharp");

        try
        {
            fixed (char* rootPathPointer = rootPath)
            fixed (char* providerNamePointer = providerName)
            fixed (char* providerVersionPointer = providerVersion)
            fixed (byte* identityPointer = syncRootIdentity)
            {
                CfSyncRegistration registration = new()
                {
                    StructSize = (uint)sizeof(CfSyncRegistration),
                    ProviderName = providerNamePointer,
                    ProviderVersion = providerVersionPointer,
                    SyncRootIdentity = identityPointer,
                    SyncRootIdentityLength = (uint)syncRootIdentity.Length,
                    ProviderId = registrationId,
                };
                CfSyncPolicies policies = new()
                {
                    StructSize = (uint)sizeof(CfSyncPolicies),
                    Hydration = new CfHydrationPolicy
                    {
                        Primary = CfHydrationPolicyPrimary.Progressive,
                        Modifier = CfHydrationPolicyModifier.AutoDehydrationAllowed,
                    },
                    Population = new CfPopulationPolicy
                    {
                        Primary = CfPopulationPolicyPrimary.Partial,
                    },
                    InSync = CfInSyncPolicy.TrackAll,
                    HardLink = CfHardLinkPolicy.None,
                    PlaceholderManagement = CfPlaceholderManagementPolicy.Default,
                };

                int registerResult = CfApi.CfRegisterSyncRoot(
                    rootPathPointer,
                    &registration,
                    &policies,
                    CfRegisterFlags.MarkInSyncOnRoot);

                Assert.Equal(0, registerResult);
                registered = true;
            }

            VerifyInformationByPath(
                rootPath,
                providerName,
                providerVersion,
                syncRootIdentity);
            VerifyInformationByHandle(handleQueryPath);

            fixed (char* rootPathPointer = rootPath)
            {
                int unregisterResult = CfApi.CfUnregisterSyncRoot(rootPathPointer);
                Assert.Equal(0, unregisterResult);
                registered = false;

                CfSyncRootBasicInfo basicInfo;
                int queryAfterUnregister = CfApi.CfGetSyncRootInfoByPath(
                    rootPathPointer,
                    CfSyncRootInfoClass.Basic,
                    &basicInfo,
                    (uint)sizeof(CfSyncRootBasicInfo),
                    null);

                Assert.True(queryAfterUnregister < 0);
            }
        }
        finally
        {
            if (registered)
            {
                fixed (char* rootPathPointer = rootPath)
                {
                    _ = CfApi.CfUnregisterSyncRoot(rootPathPointer);
                }
            }

            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static unsafe void VerifyInformationByPath(
        string rootPath,
        string expectedProviderName,
        string expectedProviderVersion,
        byte[] expectedIdentity)
    {
        fixed (char* rootPathPointer = rootPath)
        {
            CfSyncRootBasicInfo basicInfo;
            uint basicLength;
            int basicResult = CfApi.CfGetSyncRootInfoByPath(
                rootPathPointer,
                CfSyncRootInfoClass.Basic,
                &basicInfo,
                (uint)sizeof(CfSyncRootBasicInfo),
                &basicLength);

            Assert.Equal(0, basicResult);
            Assert.Equal((uint)sizeof(CfSyncRootBasicInfo), basicLength);
            Assert.NotEqual(0, basicInfo.SyncRootFileId);

            CfSyncRootProviderInfo providerInfo;
            uint providerLength;
            int providerResult = CfApi.CfGetSyncRootInfoByPath(
                rootPathPointer,
                CfSyncRootInfoClass.Provider,
                &providerInfo,
                (uint)sizeof(CfSyncRootProviderInfo),
                &providerLength);

            Assert.Equal(0, providerResult);
            Assert.Equal((uint)sizeof(CfSyncRootProviderInfo), providerLength);
            Assert.Equal(CfSyncProviderStatus.Disconnected, providerInfo.ProviderStatus);
            Assert.Equal(expectedProviderName, ReadProviderName(&providerInfo));
            Assert.Equal(expectedProviderVersion, ReadProviderVersion(&providerInfo));

            byte[] standardBuffer = new byte[sizeof(CfSyncRootStandardInfo) + expectedIdentity.Length];
            fixed (byte* standardBufferPointer = standardBuffer)
            {
                uint standardLength;
                int standardResult = CfApi.CfGetSyncRootInfoByPath(
                    rootPathPointer,
                    CfSyncRootInfoClass.Standard,
                    standardBufferPointer,
                    (uint)standardBuffer.Length,
                    &standardLength);

                Assert.Equal(0, standardResult);
                CfSyncRootStandardInfo* standardInfo =
                    (CfSyncRootStandardInfo*)standardBufferPointer;
                Assert.Equal((uint)expectedIdentity.Length, standardInfo->SyncRootIdentityLength);

                byte* returnedIdentity = standardInfo->SyncRootIdentity;
                ReadOnlySpan<byte> identity = new(
                    returnedIdentity,
                    checked((int)standardInfo->SyncRootIdentityLength));
                Assert.True(identity.SequenceEqual(expectedIdentity));
                Assert.True(standardLength >= 1056u + (uint)expectedIdentity.Length);
            }
        }
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static unsafe void VerifyInformationByHandle(string childPath)
    {
        using SafeFileHandle handle = File.OpenHandle(
            childPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        CfSyncRootBasicInfo basicInfo;
        uint returnedLength;
        int result = CfApi.CfGetSyncRootInfoByHandle(
            handle.DangerousGetHandle(),
            CfSyncRootInfoClass.Basic,
            &basicInfo,
            (uint)sizeof(CfSyncRootBasicInfo),
            &returnedLength);

        Assert.Equal(0, result);
        Assert.Equal((uint)sizeof(CfSyncRootBasicInfo), returnedLength);
        Assert.NotEqual(0, basicInfo.SyncRootFileId);
    }

    private static unsafe string ReadProviderName(CfSyncRootProviderInfo* providerInfo) =>
        new(providerInfo->ProviderName);

    private static unsafe string ReadProviderVersion(CfSyncRootProviderInfo* providerInfo) =>
        new(providerInfo->ProviderVersion);
}
