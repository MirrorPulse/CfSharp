using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using CfSharp.Native;

namespace CfSharp;

public sealed partial class CloudSyncRoot
{
    private static readonly UnicodeEncoding StrictUnicode = new(
        bigEndian: false,
        byteOrderMark: false,
        throwOnInvalidBytes: true);

    /// <summary>Reports rich provider status for this registered sync root.</summary>
    /// <param name="status">Managed status whose text and device bytes are copied for the call.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The description contains invalid data.</exception>
    /// <exception cref="PlatformNotSupportedException">Windows predates version 1803.</exception>
    /// <exception cref="CloudFilesException">Windows rejects the report.</exception>
    /// <remarks>
    /// The native buffer is temporary, Windows copies successful data, and no status is written
    /// to the CfSharp durable state store. A failed call leaves any existing Windows status intact.
    /// </remarks>
    [SupportedOSPlatform("windows10.0.17134")]
    public unsafe void ReportStatus(CloudSyncStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(status.Description);
        EnsureRichStatusSupported();

        byte[] description = EncodeDescription(status.Description);
        byte[] deviceId = status.DeviceId.ToArray();
        int headerLength = Marshal.SizeOf<CfSyncStatus>();
        int descriptionOffset = headerLength;
        int deviceIdOffset = checked(descriptionOffset + description.Length);
        int totalLength = checked(deviceIdOffset + deviceId.Length);
        byte[] buffer = new byte[totalLength];

        Buffer.BlockCopy(description, 0, buffer, descriptionOffset, description.Length);
        Buffer.BlockCopy(deviceId, 0, buffer, deviceIdOffset, deviceId.Length);
        fixed (char* pathPointer = Path)
        fixed (byte* bufferPointer = buffer)
        {
            CfSyncStatus* nativeStatus = (CfSyncStatus*)bufferPointer;
            *nativeStatus = new CfSyncStatus
            {
                StructSize = checked((uint)buffer.Length),
                Code = status.Code,
                DescriptionOffset = checked((uint)descriptionOffset),
                DescriptionLength = checked((uint)description.Length),
                DeviceIdOffset = checked((uint)deviceIdOffset),
                DeviceIdLength = checked((uint)deviceId.Length),
            };

            try
            {
                int result = CfApi.CfReportSyncStatus(pathPointer, nativeStatus);
                ThrowRichStatusIfFailed("CloudSyncRoot.ReportStatus", Path, result);
            }
            catch (DllNotFoundException exception)
            {
                throw CreateRichStatusPlatformNotSupportedException(exception);
            }
            catch (EntryPointNotFoundException exception)
            {
                throw CreateRichStatusPlatformNotSupportedException(exception);
            }
        }
    }

    /// <summary>Clears rich status previously reported for this sync root.</summary>
    /// <exception cref="PlatformNotSupportedException">Windows predates version 1803.</exception>
    /// <exception cref="CloudFilesException">Windows rejects the clear operation.</exception>
    [SupportedOSPlatform("windows10.0.17134")]
    public unsafe void ClearStatus()
    {
        EnsureRichStatusSupported();
        fixed (char* pathPointer = Path)
        {
            try
            {
                int result = CfApi.CfReportSyncStatus(pathPointer, null);
                ThrowRichStatusIfFailed("CloudSyncRoot.ClearStatus", Path, result);
            }
            catch (DllNotFoundException exception)
            {
                throw CreateRichStatusPlatformNotSupportedException(exception);
            }
            catch (EntryPointNotFoundException exception)
            {
                throw CreateRichStatusPlatformNotSupportedException(exception);
            }
        }
    }

    internal static byte[] EncodeDescription(string description)
    {
        if (description.Contains('\0'))
        {
            throw new ArgumentException("The status description cannot contain an embedded NUL.", nameof(description));
        }

        try
        {
            return StrictUnicode.GetBytes(description + '\0');
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("The status description is not valid UTF-16.", nameof(description), exception);
        }
    }

    private static void EnsureRichStatusSupported()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(
            10,
            0,
            checked((int)CloudFilesPlatformInfo.RichStatusMinimumWindowsBuild)))
        {
            throw new PlatformNotSupportedException(
                "Rich Cloud Files sync-root status requires Windows 10, version 1803 or later.");
        }
    }

    private static void ThrowRichStatusIfFailed(string operation, string path, int hresult)
    {
        if (hresult < 0)
        {
            throw CloudFilesException.FromHResult(operation, path, hresult);
        }
    }

    private static PlatformNotSupportedException CreateRichStatusPlatformNotSupportedException(
        Exception innerException) =>
        new("The installed Windows version does not expose rich Cloud Files sync-root status.", innerException);
}
