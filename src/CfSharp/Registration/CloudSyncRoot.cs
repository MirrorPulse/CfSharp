using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using CfSharp.Native;

namespace CfSharp;

/// <summary>
/// Represents a path-bound handle to a persistent Cloud Files sync-root registration.
/// </summary>
/// <remarks>
/// <para>
/// This object owns no native handle and does not automatically unregister on disposal or
/// finalization. Registration is persistent across process exits. Call <see cref="Unregister"/>
/// only for explicit account removal or product uninstall.
/// </para>
/// <para>
/// Instances are immutable and safe for concurrent use, but Windows may change the underlying
/// registration between calls. Concurrent register, update, and unregister operations for the
/// same path must be coordinated by the application.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.16299")]
public sealed partial class CloudSyncRoot
{
    private const string MinimumWindowsVersion = "windows10.0.16299";

    private CloudSyncRoot(string path)
    {
        Path = path;
    }

    /// <summary>Gets the normalized absolute sync-root path.</summary>
    public string Path { get; }

    /// <summary>Registers an existing directory as a persistent Cloud Files sync root.</summary>
    /// <param name="path">Path of the directory to register.</param>
    /// <param name="options">Immutable provider identity and policy configuration.</param>
    /// <returns>A path-bound object for querying and explicitly unregistering the root.</returns>
    /// <exception cref="ArgumentException">The path is empty or cannot be normalized.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="PlatformNotSupportedException">
    /// Windows predates version 1709 or does not expose the required Cloud Files entry points.
    /// </exception>
    /// <exception cref="CloudFilesException">
    /// Windows rejects registration. The original <c>HRESULT</c>, operation, and path are retained.
    /// </exception>
    /// <remarks>
    /// Registration survives process termination. Registering the same path and equivalent
    /// values is idempotent on Windows. To replace existing identities or policies, set
    /// <see cref="SyncRootRegistrationOptions.UpdateExisting"/>. This method does not introduce
    /// compensating unregistration after Windows reports success because the registration may
    /// have existed before the call.
    /// </remarks>
    public static CloudSyncRoot Register(string path, SyncRootRegistrationOptions options)
    {
        EnsureSupportedPlatform();
        ArgumentNullException.ThrowIfNull(options);

        string normalizedPath = NormalizePath(path);
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException(
                $"The sync-root directory does not exist: '{normalizedPath}'.");
        }

        CfSyncPolicies policies = CreateNativePolicies(options);
        CfRegisterFlags flags = CreateNativeFlags(options);

        try
        {
            int result = NativeSyncRoot.Register(
                normalizedPath,
                options.ProviderName,
                options.ProviderVersion,
                options.ProviderId,
                options.SyncRootIdentity,
                options.FileIdentity,
                policies,
                flags);
            ThrowIfFailed("CloudSyncRoot.Register", normalizedPath, result);
            return new CloudSyncRoot(normalizedPath);
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

    /// <summary>Opens a previously registered sync root at its exact root path.</summary>
    /// <param name="path">
    /// Exact registered root path. Passing a descendant path may query successfully, but is
    /// rejected as an ownership contract because unregister requires the actual root path.
    /// </param>
    /// <returns>A path-bound registration object after Windows confirms the registration.</returns>
    /// <exception cref="ArgumentException">The path is empty or cannot be normalized.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="PlatformNotSupportedException">The Cloud Files API is unavailable.</exception>
    /// <exception cref="CloudFilesException">Windows cannot query a registration at the path.</exception>
    /// <remarks>
    /// The method cannot derive the root path from an arbitrary descendant. Callers must retain
    /// the exact registered path as part of their account configuration.
    /// </remarks>
    public static CloudSyncRoot Open(string path)
    {
        EnsureSupportedPlatform();
        string normalizedPath = NormalizePath(path);
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException(
                $"The sync-root directory does not exist: '{normalizedPath}'.");
        }

        CloudSyncRoot root = new(normalizedPath);
        _ = root.GetInfo();
        return root;
    }

    /// <summary>Queries a fresh immutable snapshot of the current registration.</summary>
    /// <returns>Current provider information, policies, identity, status, and file identifier.</returns>
    /// <exception cref="PlatformNotSupportedException">The Cloud Files API is unavailable.</exception>
    /// <exception cref="CloudFilesException">Windows cannot query the registration.</exception>
    /// <exception cref="InvalidDataException">
    /// Windows returns internally inconsistent variable-length registration data.
    /// </exception>
    public CloudSyncRootInfo GetInfo()
    {
        EnsureSupportedPlatform();

        try
        {
            int result = NativeSyncRoot.Query(Path, out NativeSyncRootInfo? nativeInfo);
            ThrowIfFailed("CloudSyncRoot.GetInfo", Path, result);
            if (nativeInfo is null)
            {
                throw new InvalidDataException("Windows returned no sync-root information.");
            }

            return new CloudSyncRootInfo(
                Path,
                nativeInfo.FileId,
                nativeInfo.ProviderName,
                nativeInfo.ProviderVersion,
                nativeInfo.SyncRootIdentity,
                (CloudHydrationPolicy)nativeInfo.HydrationPolicy.Primary,
                (CloudHydrationPolicyModifiers)nativeInfo.HydrationPolicy.Modifier,
                (CloudPopulationPolicy)nativeInfo.PopulationPolicy.Primary,
                (CloudInSyncPolicy)nativeInfo.InSyncPolicy,
                nativeInfo.HardLinkPolicy == CfHardLinkPolicy.Allowed
                    ? CloudHardLinkPolicy.Allowed
                    : CloudHardLinkPolicy.Disallowed,
                (CloudProviderStatus)nativeInfo.ProviderStatus);
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

    /// <summary>Creates an online-only file placeholder beneath this sync root.</summary>
    /// <param name="relativePath">
    /// Non-rooted path beneath the sync root. Its parent directory must already exist.
    /// </param>
    /// <param name="fileSize">Non-negative logical size of the remote file in bytes.</param>
    /// <param name="fileIdentity">
    /// Provider-defined identity returned with future callbacks. The value is copied by Windows
    /// and cannot exceed <see cref="SyncRootRegistrationOptions.MaxFileIdentityLength"/> bytes.
    /// </param>
    /// <returns>The normalized created path and its creation update sequence number.</returns>
    /// <exception cref="ArgumentException">The path is empty, rooted, or escapes the sync root.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fileSize"/> is negative.</exception>
    /// <exception cref="DirectoryNotFoundException">The placeholder's parent directory is absent.</exception>
    /// <exception cref="CloudFilesException">Windows rejects the placeholder creation.</exception>
    /// <remarks>
    /// The placeholder is marked in sync and initially contains no local data. Reading it requires
    /// an active <see cref="CloudProviderSession"/> capable of supplying its identity's content.
    /// This method is thread-safe for distinct paths; callers must coordinate competing operations
    /// targeting the same path.
    /// </remarks>
    public unsafe CloudPlaceholderCreationResult CreateFilePlaceholder(
        string relativePath,
        long fileSize,
        ReadOnlySpan<byte> fileIdentity)
    {
        EnsureSupportedPlatform();
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);
        if (System.IO.Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("The placeholder path must be relative.", nameof(relativePath));
        }

        if (fileIdentity.Length > SyncRootRegistrationOptions.MaxFileIdentityLength)
        {
            throw new ArgumentException(
                $"The file identity cannot exceed {SyncRootRegistrationOptions.MaxFileIdentityLength} bytes.",
                nameof(fileIdentity));
        }

        CloudItemPath resolvedPath = CloudItemPathResolver.Resolve(
            Path,
            relativePath,
            allowRoot: false);
        string targetPath = resolvedPath.FullPath;

        string? parentPath = System.IO.Path.GetDirectoryName(targetPath);
        if (parentPath is null || !Directory.Exists(parentPath))
        {
            throw new DirectoryNotFoundException(
                $"The placeholder parent directory does not exist: '{parentPath}'.");
        }

        string normalizedRelativePath = resolvedPath.RelativePath;
        fixed (char* rootPathPointer = Path)
        fixed (char* relativePathPointer = normalizedRelativePath)
        fixed (byte* identityPointer = fileIdentity)
        {
            CfPlaceholderCreateInfo placeholder = new()
            {
                RelativeFileName = relativePathPointer,
                FsMetadata = new CfFsMetadata
                {
                    BasicInfo = new CfFileBasicInfo
                    {
                        FileAttributes = (uint)FileAttributes.Normal,
                    },
                    FileSize = fileSize,
                },
                FileIdentity = identityPointer,
                FileIdentityLength = (uint)fileIdentity.Length,
                Flags = CfPlaceholderCreateFlags.MarkInSync,
            };
            uint entriesProcessed;
            int result = CfApi.CfCreatePlaceholders(
                rootPathPointer,
                &placeholder,
                1,
                CfCreateFlags.StopOnError,
                &entriesProcessed);
            ThrowIfFailed("CloudSyncRoot.CreateFilePlaceholder", targetPath, result);
            if (entriesProcessed != 1)
            {
                throw new InvalidDataException("Windows did not process the placeholder entry.");
            }

            ThrowIfFailed("CloudSyncRoot.CreateFilePlaceholder", targetPath, placeholder.Result);
            return new CloudPlaceholderCreationResult(targetPath, placeholder.CreateUsn);
        }
    }

    /// <summary>Permanently unregisters this sync root.</summary>
    /// <exception cref="PlatformNotSupportedException">The Cloud Files API is unavailable.</exception>
    /// <exception cref="CloudFilesException">
    /// Windows rejects unregistration, including when a provider remains connected or the path
    /// is no longer registered.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is destructive lifecycle behavior, not session shutdown. Windows traverses the
    /// tree, reverts fully available placeholders, and may permanently remove incomplete
    /// placeholders. A successful call does not delete the root directory itself.
    /// </para>
    /// <para>The object remains usable as a path value, but subsequent queries will fail.</para>
    /// </remarks>
    public void Unregister()
    {
        EnsureSupportedPlatform();

        try
        {
            int result = NativeSyncRoot.Unregister(Path);
            ThrowIfFailed("CloudSyncRoot.Unregister", Path, result);
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

    private static CfSyncPolicies CreateNativePolicies(SyncRootRegistrationOptions options) =>
        new()
        {
            StructSize = (uint)Marshal.SizeOf<CfSyncPolicies>(),
            Hydration = new CfHydrationPolicy
            {
                Primary = (CfHydrationPolicyPrimary)options.HydrationPolicy,
                Modifier = (CfHydrationPolicyModifier)options.HydrationModifiers,
            },
            Population = new CfPopulationPolicy
            {
                Primary = (CfPopulationPolicyPrimary)options.PopulationPolicy,
                Modifier = CfPopulationPolicyModifier.None,
            },
            InSync = (CfInSyncPolicy)options.InSyncPolicy,
            HardLink = options.HardLinkPolicy == CloudHardLinkPolicy.Allowed
                ? CfHardLinkPolicy.Allowed
                : CfHardLinkPolicy.None,
            PlaceholderManagement =
                (CfPlaceholderManagementPolicy)options.PlaceholderManagementPolicy,
        };

    private static CfRegisterFlags CreateNativeFlags(SyncRootRegistrationOptions options)
    {
        CfRegisterFlags flags = CfRegisterFlags.None;
        if (options.UpdateExisting)
        {
            flags |= CfRegisterFlags.Update;
        }

        if (options.DisableOnDemandPopulationOnRoot)
        {
            flags |= CfRegisterFlags.DisableOnDemandPopulationOnRoot;
        }

        if (options.MarkRootInSync)
        {
            flags |= CfRegisterFlags.MarkInSyncOnRoot;
        }

        return flags;
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The sync-root path is invalid.", nameof(path), exception);
        }
    }

    private static void EnsureSupportedPlatform()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            throw new PlatformNotSupportedException(
                "The Windows Cloud Files API requires Windows 10, version 1709 or later.");
        }
    }

    private static void ThrowIfFailed(string operation, string path, int hresult)
    {
        if (hresult < 0)
        {
            throw CloudFilesException.FromHResult(operation, path, hresult);
        }
    }

    private static PlatformNotSupportedException CreatePlatformNotSupportedException(
        Exception innerException) =>
        new("The installed Windows version does not expose the required Cloud Files API.", innerException);
}
