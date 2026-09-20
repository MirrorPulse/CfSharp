using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CfSharp.Native;

/// <summary>
/// Exposes direct native entry points from the Windows Cloud Files API.
/// </summary>
/// <remarks>
/// <para>
/// Members of this class preserve native signatures and error values for callers that
/// require direct CFAPI access. They do not translate <c>HRESULT</c> failures into
/// managed exceptions.
/// </para>
/// <para>
/// Unless a member states otherwise, arguments follow the ownership and lifetime rules
/// documented for the corresponding function in <c>cfapi.h</c>. The entry points are
/// loaded from the copy of <c>CldApi.dll</c> in the Windows system directory.
/// </para>
/// </remarks>
public static partial class CfApi
{
    /// <summary>Maximum provider display-name length, excluding the terminating null.</summary>
    public const int MaxProviderNameLength = 255;

    /// <summary>Maximum provider version length, excluding the terminating null.</summary>
    public const int MaxProviderVersionLength = 255;

    /// <summary>Maximum placeholder file-identity length in bytes.</summary>
    public const int MaxFileIdentityLength = 4096;

    /// <summary>Maximum callback priority hint defined by the Cloud Files API.</summary>
    public const byte MaxPriorityHint = 15;

    /// <summary>
    /// Retrieves version and capability information for the installed Cloud Files platform.
    /// </summary>
    /// <param name="platformVersion">
    /// Receives a self-contained platform information value. The caller owns the value and
    /// no cleanup is required, regardless of whether the function succeeds.
    /// </param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This function is available on Windows 10, version 1709 and later. Calling it on a
    /// system where <c>CldApi.dll</c> or the export is unavailable may produce the standard
    /// .NET native-library loading exceptions before an <c>HRESULT</c> can be returned.
    /// </para>
    /// <para>
    /// The function has no retained callback or buffer lifetime and may be invoked
    /// concurrently from multiple threads.
    /// </para>
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetPlatformInfo))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    public static partial int CfGetPlatformInfo(out CfPlatformInfo platformVersion);

    /// <summary>
    /// Persistently registers a directory tree as a Cloud Files sync root.
    /// </summary>
    /// <param name="syncRootPath">
    /// Pointer to a null-terminated, fully qualified UTF-16 path. The directory must exist,
    /// reside on a supported file system, and be writable by the caller.
    /// </param>
    /// <param name="registration">
    /// Pointer to initialized provider and identity data. The pointed-to structure, strings,
    /// and identity buffers need remain valid only until this call returns.
    /// </param>
    /// <param name="policies">
    /// Pointer to initialized sync-root policies. The pointed-to structure need remain valid
    /// only until this call returns.
    /// </param>
    /// <param name="registerFlags">Flags controlling creation or update behavior.</param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Registration is persistent and survives process termination. A successful call must
    /// eventually be paired with an explicit <see cref="CfUnregisterSyncRoot"/> when the
    /// account or product registration is removed. Overlapping sync-root trees are rejected.
    /// </para>
    /// <para>
    /// Provider name and version are limited to 255 characters. Sync-root identity is
    /// limited to 64 KiB and file identity to <see cref="MaxFileIdentityLength"/> bytes.
    /// The caller owns every input buffer; Windows copies retained registration data before
    /// returning. Independent registrations may be performed concurrently.
    /// </para>
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfRegisterSyncRoot))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfRegisterSyncRoot(
        char* syncRootPath,
        CfSyncRegistration* registration,
        CfSyncPolicies* policies,
        CfRegisterFlags registerFlags);

    /// <summary>
    /// Persistently unregisters a previously registered Cloud Files sync root.
    /// </summary>
    /// <param name="syncRootPath">
    /// Pointer to the null-terminated, fully qualified UTF-16 path of the registered root.
    /// The pointer need remain valid only until this call returns.
    /// </param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The operation fails while a provider is connected. It traverses the tree: fully
    /// hydrated placeholders are reverted to normal items, while incomplete placeholders
    /// may be permanently removed. Callers should therefore treat this as account-removal
    /// or uninstall behavior rather than process-session cleanup.
    /// </para>
    /// <para>The function retains no buffers and may be called concurrently for separate roots.</para>
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfUnregisterSyncRoot))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfUnregisterSyncRoot(char* syncRootPath);

    /// <summary>
    /// Queries the sync root containing a file or directory identified by path.
    /// </summary>
    /// <param name="filePath">
    /// Pointer to a null-terminated, fully qualified UTF-16 path beneath a registered sync
    /// root. The pointer need remain valid only until this call returns.
    /// </param>
    /// <param name="infoClass">Selects the native result structure written to the buffer.</param>
    /// <param name="infoBuffer">
    /// Pointer to caller-owned writable storage appropriate for <paramref name="infoClass"/>.
    /// </param>
    /// <param name="infoBufferLength">Available buffer length in bytes.</param>
    /// <param name="returnedLength">
    /// Optional pointer receiving the number of result bytes. Pass <see langword="null"/>
    /// when the length is not required.
    /// </param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure, including when the path is outside a sync root.
    /// </returns>
    /// <remarks>
    /// The function does not retain the path or result buffer and may be called concurrently.
    /// Standard information can contain a variable-length trailing identity; callers must
    /// allocate and interpret that result according to <see cref="CfSyncRootStandardInfo"/>.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetSyncRootInfoByPath))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfGetSyncRootInfoByPath(
        char* filePath,
        CfSyncRootInfoClass infoClass,
        void* infoBuffer,
        uint infoBufferLength,
        uint* returnedLength);

    /// <summary>
    /// Queries the sync root containing a file or directory identified by an open handle.
    /// </summary>
    /// <param name="fileHandle">
    /// Valid file or directory handle with at least read-attributes access. Ownership remains
    /// with the caller; this function neither closes nor retains it.
    /// </param>
    /// <param name="infoClass">Selects the native result structure written to the buffer.</param>
    /// <param name="infoBuffer">
    /// Pointer to caller-owned writable storage appropriate for <paramref name="infoClass"/>.
    /// </param>
    /// <param name="infoBufferLength">Available buffer length in bytes.</param>
    /// <param name="returnedLength">
    /// Optional pointer receiving the number of result bytes. Pass <see langword="null"/>
    /// when the length is not required.
    /// </param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure, including when the handle is outside a sync root.
    /// </returns>
    /// <remarks>
    /// The function is observational and does not modify the file. It does not retain the
    /// handle or result buffer and may be called concurrently. Standard information can
    /// contain a variable-length trailing identity.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetSyncRootInfoByHandle))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfGetSyncRootInfoByHandle(
        nint fileHandle,
        CfSyncRootInfoClass infoClass,
        void* infoBuffer,
        uint infoBufferLength,
        uint* returnedLength);

    /// <summary>Creates one or more placeholder files or directories beneath a sync root.</summary>
    /// <param name="baseDirectoryPath">
    /// Pointer to a null-terminated UTF-16 path under a registered sync root. Every entry name
    /// is interpreted relative to this directory.
    /// </param>
    /// <param name="placeholderArray">
    /// Pointer to a mutable array of <paramref name="placeholderCount"/> entries. Windows writes
    /// each entry's result and creation USN before returning.
    /// </param>
    /// <param name="placeholderCount">Number of entries in <paramref name="placeholderArray"/>.</param>
    /// <param name="createFlags">Flags controlling failure behavior for the complete batch.</param>
    /// <param name="entriesProcessed">
    /// Optional pointer receiving the number of entries processed. Pass <see langword="null"/>
    /// when the count is not required.
    /// </param>
    /// <returns>
    /// The native batch-level <c>HRESULT</c> without translation. Inspect every processed entry's
    /// <see cref="CfPlaceholderCreateInfo.Result"/> when the call permits partial success.
    /// </returns>
    /// <remarks>
    /// The function retains no pointers. The array, relative names, and identity buffers must
    /// remain valid only until it returns. Entry identities cannot exceed
    /// <see cref="MaxFileIdentityLength"/> bytes.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfCreatePlaceholders))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfCreatePlaceholders(
        char* baseDirectoryPath,
        CfPlaceholderCreateInfo* placeholderArray,
        uint placeholderCount,
        CfCreateFlags createFlags,
        uint* entriesProcessed);

    /// <summary>
    /// Connects a registered sync root to a provider callback table.
    /// </summary>
    /// <param name="syncRootPath">Pointer to the null-terminated UTF-16 registered root path.</param>
    /// <param name="callbackTable">
    /// Pointer to an array terminated by <see cref="CfCallbackType.None"/>. Windows retains
    /// access to the array and its function pointers for the complete connection lifetime.
    /// </param>
    /// <param name="callbackContext">
    /// Optional provider-owned context pointer returned in each <see cref="CfCallbackInfo"/>.
    /// Windows does not own or release the target.
    /// </param>
    /// <param name="connectFlags">Flags controlling callback information and hydration behavior.</param>
    /// <param name="connectionKey">Receives the opaque key for the new connection.</param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Only one provider may be connected to a given sync root at a time. Callbacks can occur
    /// concurrently on platform threads as soon as this function succeeds.
    /// </para>
    /// <para>
    /// The path need remain valid only for this call. The callback table, callback entry points,
    /// and callback context target must remain valid until <see cref="CfDisconnectSyncRoot"/>
    /// returns. Exceptions must never escape an unmanaged callback entry point.
    /// </para>
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfConnectSyncRoot))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfConnectSyncRoot(
        char* syncRootPath,
        CfCallbackRegistration* callbackTable,
        void* callbackContext,
        CfConnectFlags connectFlags,
        out CfConnectionKey connectionKey);

    /// <summary>Disconnects a provider communication channel from its sync root.</summary>
    /// <param name="connectionKey">Opaque key returned by <see cref="CfConnectSyncRoot"/>.</param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure.
    /// </returns>
    /// <remarks>
    /// Callbacks may still arrive while this function is executing. After it returns, Windows
    /// no longer invokes the registered callbacks, so the provider may release the callback
    /// table and context. Unexpected process termination is also detected and cleaned up by
    /// Windows, but explicit disconnection provides deterministic shutdown.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfDisconnectSyncRoot))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native key contract.")]
    public static partial int CfDisconnectSyncRoot(CfConnectionKey connectionKey);

    /// <summary>Completes or advances a Cloud Files callback operation.</summary>
    /// <param name="operationInfo">
    /// Pointer to operation identity copied from the callback. Its type selects the active
    /// branch in <paramref name="operationParameters"/>.
    /// </param>
    /// <param name="operationParameters">
    /// Pointer to mutable operation parameters with a branch-specific
    /// <see cref="CfOperationParameters.ParamSize"/>. Windows may update output fields.
    /// </param>
    /// <returns>
    /// The native call-level <c>HRESULT</c> without translation. For completion operations, the
    /// request outcome is carried separately by the branch's <see cref="NtStatus"/> value.
    /// </returns>
    /// <remarks>
    /// Input buffers need remain valid until this call returns. A successful return means Windows
    /// accepted this operation; it does not replace the terminal status supplied for the request.
    /// Calls for separate requests may execute concurrently.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfExecute))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfExecute(
        CfOperationInfo* operationInfo,
        CfOperationParameters* operationParameters);

    /// <summary>Updates the activity or terminal status reported for a connected provider.</summary>
    /// <param name="connectionKey">Opaque key of the active provider connection.</param>
    /// <param name="providerStatus">Status flags or terminal state to report.</param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure.
    /// </returns>
    /// <remarks>The function retains no managed memory and may be called concurrently.</remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfUpdateSyncProviderStatus))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native key contract.")]
    public static partial int CfUpdateSyncProviderStatus(
        CfConnectionKey connectionKey,
        CfSyncProviderStatus providerStatus);

    /// <summary>Queries the activity or terminal status of a connected provider.</summary>
    /// <param name="connectionKey">Opaque key of the active provider connection.</param>
    /// <param name="providerStatus">Receives the current provider status.</param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure.
    /// </returns>
    /// <remarks>The function retains no managed memory and may be called concurrently.</remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfQuerySyncProviderStatus))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    public static partial int CfQuerySyncProviderStatus(
        CfConnectionKey connectionKey,
        out CfSyncProviderStatus providerStatus);
}
