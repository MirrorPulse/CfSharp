using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Provider;

[SupportedOSPlatform("windows10.0.19041")]
internal static class ShellSyncRootRegistrar
{
    private const string DisplayName = "CfSharp Sample";
    private static readonly Guid ProviderId = new("BD3DAA90-BDEA-48E4-8257-A0C7D35A6803");

    internal static async Task<bool> RegisterAsync(string syncRootPath)
    {
        if (!StorageProviderSyncRootManager.IsSupported())
        {
            throw new PlatformNotSupportedException(
                "Windows Shell sync-root registration is not supported on this system.");
        }

        bool alreadyRegistered = TryGetRegisteredPath(out string? existingPath);
        if (alreadyRegistered && !string.Equals(
                Path.GetFullPath(existingPath!),
                Path.GetFullPath(syncRootPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The CfSharp Sample Shell registration already belongs to '{existingPath}'.");
        }

        StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(syncRootPath);
        StorageProviderSyncRootInfo registration = new()
        {
            Id = GetRegistrationId(),
            Path = folder,
            ProviderId = ProviderId,
            DisplayNameResource = DisplayName,
            IconResource = "%SystemRoot%\\System32\\imageres.dll,-102",
            HydrationPolicy = StorageProviderHydrationPolicy.Progressive,
            HydrationPolicyModifier = StorageProviderHydrationPolicyModifier.AutoDehydrationAllowed,
            PopulationPolicy = StorageProviderPopulationPolicy.Full,
            InSyncPolicy = StorageProviderInSyncPolicy.FileCreationTime |
                StorageProviderInSyncPolicy.DirectoryCreationTime,
            HardlinkPolicy = StorageProviderHardlinkPolicy.None,
            Version = "0.1.0",
            AllowPinning = true,
            ShowSiblingsAsGroup = false,
            Context = CryptographicBuffer.ConvertStringToBinary(
                syncRootPath,
                BinaryStringEncoding.Utf8),
        };

        StorageProviderSyncRootManager.Register(registration);
        return alreadyRegistered;
    }

    internal static void Unregister(string expectedSyncRootPath)
    {
        if (!TryGetRegisteredPath(out string? existingPath))
        {
            return;
        }

        if (!string.Equals(
                Path.GetFullPath(existingPath!),
                Path.GetFullPath(expectedSyncRootPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The CfSharp Sample Shell registration belongs to '{existingPath}', not '{expectedSyncRootPath}'.");
        }

        StorageProviderSyncRootManager.Unregister(GetRegistrationId());
    }

    internal static bool TryGetRegisteredPath(out string? path)
    {
        try
        {
            StorageProviderSyncRootInfo registration =
                StorageProviderSyncRootManager.GetSyncRootInformationForId(GetRegistrationId());
            path = registration.Path.Path;
            return true;
        }
        catch (COMException)
        {
            path = null;
            return false;
        }
    }

    internal static string GetRegistrationId()
    {
        string userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows identity has no SID.");
        return $"CfSharpSample!{userSid}!Default";
    }
}
