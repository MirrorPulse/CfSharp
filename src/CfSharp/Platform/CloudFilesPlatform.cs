using System.Runtime.Versioning;
using CfSharp.Native;

namespace CfSharp;

/// <summary>
/// Provides information about the Cloud Files platform installed with Windows.
/// </summary>
/// <remarks>
/// This type is stateless, owns no native resources, and is safe for concurrent use.
/// </remarks>
public static class CloudFilesPlatform
{
    private const string MinimumWindowsVersion = "windows10.0.16299";

    /// <summary>
    /// Gets the current Cloud Files platform version and capability level.
    /// </summary>
    /// <returns>
    /// An immutable snapshot of the platform information returned by Windows. The value owns
    /// no native resources and remains valid independently of subsequent calls.
    /// </returns>
    /// <exception cref="PlatformNotSupportedException">
    /// The operating system predates Windows 10, version 1709, or does not expose the required
    /// Cloud Files native library entry point.
    /// </exception>
    /// <exception cref="CloudFilesException">
    /// Windows returned a failing <c>HRESULT</c>. The exception retains the original result and
    /// any embedded Win32 error code.
    /// </exception>
    /// <remarks>
    /// The method does not cache platform state and may be called concurrently. Applications
    /// should normally query once during startup and retain the returned value for capability
    /// decisions.
    /// </remarks>
    [SupportedOSPlatform(MinimumWindowsVersion)]
    public static CloudFilesPlatformInfo GetCurrent()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            throw new PlatformNotSupportedException(
                "The Windows Cloud Files API requires Windows 10, version 1709 or later.");
        }

        try
        {
            int result = CfApi.CfGetPlatformInfo(out CfPlatformInfo nativeInfo);
            if (result < 0)
            {
                throw CloudFilesException.FromHResult(nameof(CfApi.CfGetPlatformInfo), result);
            }

            return new CloudFilesPlatformInfo(
                nativeInfo.BuildNumber,
                nativeInfo.RevisionNumber,
                nativeInfo.IntegrationNumber);
        }
        catch (DllNotFoundException exception)
        {
            throw CreatePlatformNotSupportedException(exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw CreatePlatformNotSupportedException(exception);
        }
    }

    private static PlatformNotSupportedException CreatePlatformNotSupportedException(Exception innerException) =>
        new("The installed Windows version does not expose the required Cloud Files API.", innerException);
}
