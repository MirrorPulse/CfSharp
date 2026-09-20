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
    /// <summary>
    /// Sentinel length indicating that an operation extends from its starting offset to the
    /// logical end of the file. Mirrors <c>CF_EOF</c>.
    /// </summary>
    public const long EndOfFile = -1;

    /// <summary>
    /// Default request-key value for operations that are not associated with a callback request.
    /// Mirrors <c>CF_REQUEST_KEY_DEFAULT</c>.
    /// </summary>
    public const long DefaultRequestKey = 0;

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

    /// <summary>Opens a file or directory as an opaque Cloud Files protected handle.</summary>
    /// <param name="filePath">
    /// Pointer to a null-terminated, fully qualified UTF-16 path. The pointer need remain valid
    /// only until this call returns.
    /// </param>
    /// <param name="flags">Access, sharing, and oplock behavior for the open.</param>
    /// <param name="protectedHandle">
    /// Receives an opaque protected handle. On success, the caller owns one reference and must
    /// close it exactly once with <see cref="CfCloseHandle"/>. It must not be passed directly to
    /// general Win32 APIs.
    /// </param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Unless <see cref="CfOpenFileFlags.Foreground"/> is specified, Windows requests an oplock
    /// and may invalidate the protected handle after draining active references when that oplock
    /// breaks. The handle can represent either an ordinary item or a placeholder.
    /// </para>
    /// <para>
    /// Call <see cref="CfReferenceProtectedHandle"/> before borrowing its Win32 handle and pair
    /// every successful reference with <see cref="CfReleaseProtectedHandle"/>. Closing the
    /// protected handle while another thread uses it requires caller synchronization.
    /// </para>
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfOpenFileWithOplock))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfOpenFileWithOplock(
        char* filePath,
        CfOpenFileFlags flags,
        out nint protectedHandle);

    /// <summary>Acquires a temporary reference that prevents a protected handle from closing.</summary>
    /// <param name="protectedHandle">Opaque handle returned by <see cref="CfOpenFileWithOplock"/>.</param>
    /// <returns>A nonzero native <c>BOOLEAN</c> on success; otherwise zero.</returns>
    /// <remarks>
    /// Every successful call must be paired with <see cref="CfReleaseProtectedHandle"/>. Keep the
    /// reference short-lived because it delays oplock-break acknowledgement and can block other
    /// applications from opening the item.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfReferenceProtectedHandle))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally preserves the one-byte BOOLEAN return value.")]
    public static partial byte CfReferenceProtectedHandle(nint protectedHandle);

    /// <summary>Gets the borrowed Win32 handle underlying a referenced protected handle.</summary>
    /// <param name="protectedHandle">
    /// Opaque handle with a currently held reference from <see cref="CfReferenceProtectedHandle"/>.
    /// </param>
    /// <returns>
    /// The borrowed Win32 handle. The caller must not close it and must stop using it before
    /// releasing the corresponding protected-handle reference.
    /// </returns>
    /// <remarks>
    /// This function returns a raw handle rather than an <c>HRESULT</c>. Callers must treat an
    /// invalid handle value as failure. Concurrent release or closure requires external
    /// synchronization.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetWin32HandleFromProtectedHandle))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the borrowed native handle.")]
    public static partial nint CfGetWin32HandleFromProtectedHandle(nint protectedHandle);

    /// <summary>Releases one reference acquired with <see cref="CfReferenceProtectedHandle"/>.</summary>
    /// <param name="protectedHandle">Opaque protected handle whose reference is released.</param>
    /// <remarks>
    /// The call has no return value. The protected handle remains owned by its original caller and
    /// still requires <see cref="CfCloseHandle"/>. Do not use the borrowed Win32 handle after this
    /// call.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfReleaseProtectedHandle))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the native protected-handle lifetime.")]
    public static partial void CfReleaseProtectedHandle(nint protectedHandle);

    /// <summary>Closes a protected handle returned by <see cref="CfOpenFileWithOplock"/>.</summary>
    /// <param name="fileHandle">
    /// Opaque protected handle to close. This must not be an ordinary Win32 handle.
    /// </param>
    /// <remarks>
    /// The call has no return value. The caller must ensure all references acquired through
    /// <see cref="CfReferenceProtectedHandle"/> have first been released and must not use the
    /// protected handle again after this call.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfCloseHandle))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the native protected-handle lifetime.")]
    public static partial void CfCloseHandle(nint fileHandle);

    /// <summary>Converts an existing ordinary file or directory into a Cloud Files placeholder.</summary>
    /// <param name="fileHandle">
    /// Open Win32 handle to an item within a registered sync root. Write-data or write-DAC access
    /// is required. When dehydration is requested, the handle must provide exclusive access.
    /// </param>
    /// <param name="fileIdentity">
    /// Optional pointer to caller-owned opaque identity bytes. The buffer need remain valid until
    /// synchronous completion or until an asynchronous operation completes.
    /// </param>
    /// <param name="fileIdentityLength">
    /// Length of <paramref name="fileIdentity"/> in bytes; cannot exceed
    /// <see cref="MaxFileIdentityLength"/>.
    /// </param>
    /// <param name="convertFlags">Flags controlling synchronization, hydration, and population state.</param>
    /// <param name="convertUsn">
    /// Optional pointer receiving the final update sequence number after conversion.
    /// </param>
    /// <param name="overlapped">
    /// Optional caller-owned native overlapped state. When supplied with an asynchronous handle,
    /// it and every input buffer must remain valid until completion is observed.
    /// </param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A pending asynchronous operation is reported
    /// as the HRESULT form of <c>ERROR_IO_PENDING</c>.
    /// </returns>
    /// <remarks>
    /// The operation mutates file-system state. <see cref="CfConvertFlags.Dehydrate"/> requires an
    /// unpinned, in-sync item and a sync-root hydration policy that permits dehydration. The
    /// platform does not verify that the caller's handle is exclusive.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfConvertToPlaceholder))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfConvertToPlaceholder(
        nint fileHandle,
        void* fileIdentity,
        uint fileIdentityLength,
        CfConvertFlags convertFlags,
        long* convertUsn,
        NativeOverlapped* overlapped);

    /// <summary>Updates metadata, identity, validity ranges, or state on an existing placeholder.</summary>
    /// <param name="fileHandle">
    /// Open Win32 handle to the placeholder with write-data or write-DAC access. Dehydrating an
    /// updated file requires an exclusive handle.
    /// </param>
    /// <param name="fsMetadata">
    /// Optional pointer to replacement file-system metadata. Unless passthrough is requested,
    /// zero-valued timestamps and attributes mean unchanged; a zero file size still means zero.
    /// </param>
    /// <param name="fileIdentity">Optional pointer to replacement opaque identity bytes.</param>
    /// <param name="fileIdentityLength">
    /// Length of <paramref name="fileIdentity"/> in bytes; cannot exceed
    /// <see cref="MaxFileIdentityLength"/>.
    /// </param>
    /// <param name="dehydrateRangeArray">
    /// Optional pointer to page-aligned ranges whose local content becomes invalid.
    /// </param>
    /// <param name="dehydrateRangeCount">Number of entries in <paramref name="dehydrateRangeArray"/>.</param>
    /// <param name="updateFlags">Flags selecting the placeholder changes and preconditions.</param>
    /// <param name="updateUsn">
    /// Optional input/output update sequence number. A nonzero input makes the operation
    /// conditional; on success Windows writes the final value.
    /// </param>
    /// <param name="overlapped">
    /// Optional caller-owned native overlapped state. For asynchronous completion, all pointed-to
    /// buffers and the structure must remain valid until completion is observed.
    /// </param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation, including the HRESULT form of
    /// <c>ERROR_IO_PENDING</c> for an asynchronous operation.
    /// </returns>
    /// <remarks>
    /// The operation is atomic with respect to its requested range invalidation: if any supplied
    /// range cannot be dehydrated, the update fails rather than leaving torn content. Supplying
    /// <see cref="CfUpdateFlags.Dehydrate"/> causes the range array to be ignored.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfUpdatePlaceholder))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfUpdatePlaceholder(
        nint fileHandle,
        CfFsMetadata* fsMetadata,
        void* fileIdentity,
        uint fileIdentityLength,
        CfFileRange* dehydrateRangeArray,
        uint dehydrateRangeCount,
        CfUpdateFlags updateFlags,
        long* updateUsn,
        NativeOverlapped* overlapped);

    /// <summary>Reverts a placeholder to an ordinary file or directory.</summary>
    /// <param name="fileHandle">
    /// Open Win32 handle to the placeholder with write-data or write-DAC access.
    /// </param>
    /// <param name="revertFlags">Revert behavior; only <see cref="CfRevertFlags.None"/> is defined.</param>
    /// <param name="overlapped">
    /// Optional caller-owned native overlapped state that must remain valid through asynchronous
    /// completion.
    /// </param>
    /// <returns>The native <c>HRESULT</c> without translation.</returns>
    /// <remarks>
    /// Reversion permanently removes the Cloud Files reparse data and identity. Windows first
    /// hydrates incomplete file content, which can invoke the connected provider and fail if the
    /// content cannot be obtained.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfRevertPlaceholder))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native handle contract.")]
    public static unsafe partial int CfRevertPlaceholder(
        nint fileHandle,
        CfRevertFlags revertFlags,
        NativeOverlapped* overlapped);

    /// <summary>Ensures that a byte range of a placeholder file is present locally.</summary>
    /// <param name="fileHandle">
    /// Open Win32 handle to the placeholder with read-data or write-DAC access.
    /// </param>
    /// <param name="startingOffset">Zero-based first byte to hydrate.</param>
    /// <param name="length">
    /// Number of bytes to hydrate, or <see cref="EndOfFile"/> to continue to logical end of file.
    /// </param>
    /// <param name="hydrateFlags">Hydration behavior; only <see cref="CfHydrateFlags.None"/> is defined.</param>
    /// <param name="overlapped">
    /// Optional caller-owned native overlapped state that must remain valid through asynchronous
    /// completion.
    /// </param>
    /// <returns>The native <c>HRESULT</c> without translation.</returns>
    /// <remarks>
    /// Missing ranges cause fetch callbacks to the connected provider. A null overlapped pointer
    /// makes the operation synchronous even when the handle was opened for asynchronous I/O.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfHydratePlaceholder))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native handle contract.")]
    public static unsafe partial int CfHydratePlaceholder(
        nint fileHandle,
        long startingOffset,
        long length,
        CfHydrateFlags hydrateFlags,
        NativeOverlapped* overlapped);

    /// <summary>Removes locally present content from a byte range of a placeholder file.</summary>
    /// <param name="fileHandle">
    /// Open Win32 handle to an unpinned, in-sync placeholder that permits dehydration.
    /// </param>
    /// <param name="startingOffset">Zero-based first byte to dehydrate.</param>
    /// <param name="length">
    /// Number of bytes to dehydrate, or <see cref="EndOfFile"/> to continue to logical end of file.
    /// </param>
    /// <param name="dehydrateFlags">Flags describing foreground or background dehydration.</param>
    /// <param name="overlapped">
    /// Optional caller-owned native overlapped state that must remain valid through asynchronous
    /// completion.
    /// </param>
    /// <returns>The native <c>HRESULT</c> without translation.</returns>
    /// <remarks>
    /// The platform may expand a non-page-aligned requested range to page boundaries. Dehydration
    /// changes availability only; the placeholder identity and metadata remain present.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfDehydratePlaceholder))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native handle contract.")]
    public static unsafe partial int CfDehydratePlaceholder(
        nint fileHandle,
        long startingOffset,
        long length,
        CfDehydrateFlags dehydrateFlags,
        NativeOverlapped* overlapped);

    /// <summary>Sets the user's requested pin state for a placeholder.</summary>
    /// <param name="fileHandle">Open Win32 handle to the placeholder.</param>
    /// <param name="pinState">Requested local-availability state.</param>
    /// <param name="pinFlags">Flags controlling recursive directory application.</param>
    /// <param name="overlapped">
    /// Optional caller-owned native overlapped state that must remain valid through asynchronous
    /// completion.
    /// </param>
    /// <returns>The native <c>HRESULT</c> without translation.</returns>
    /// <remarks>
    /// This API records user intent and may be called by applications other than the provider.
    /// Recursive calls can partially apply unless <see cref="CfSetPinFlags.RecurseStopOnError"/>
    /// is specified; the native API does not return per-descendant results.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfSetPinState))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native handle contract.")]
    public static unsafe partial int CfSetPinState(
        nint fileHandle,
        CfPinState pinState,
        CfSetPinFlags pinFlags,
        NativeOverlapped* overlapped);

    /// <summary>Sets whether a placeholder agrees with its provider state.</summary>
    /// <param name="fileHandle">
    /// Open Win32 handle to the placeholder with write-data or write-DAC access.
    /// </param>
    /// <param name="inSyncState">New synchronization state.</param>
    /// <param name="inSyncFlags">
    /// State-update flags; only <see cref="CfSetInSyncFlags.None"/> is currently defined.
    /// </param>
    /// <param name="inSyncUsn">
    /// Optional input/output update sequence number. A nonzero input makes the change conditional;
    /// on success Windows writes the final value.
    /// </param>
    /// <returns>The native <c>HRESULT</c> without translation.</returns>
    /// <remarks>
    /// The function is synchronous. Callers coordinating an upload acknowledgement should pass
    /// the observed USN to avoid marking a locally changed item in sync.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfSetInSyncState))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native handle contract.")]
    public static unsafe partial int CfSetInSyncState(
        nint fileHandle,
        CfInSyncState inSyncState,
        CfSetInSyncFlags inSyncFlags,
        long* inSyncUsn);

    /// <summary>Associates a provider-selected correlation vector with a placeholder.</summary>
    /// <param name="fileHandle">Open Win32 handle to the placeholder.</param>
    /// <param name="correlationVector">
    /// Pointer to a caller-owned initialized vector. The pointer need remain valid only until the
    /// call returns because Windows copies the value.
    /// </param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. An uninitialized or malformed vector is
    /// rejected with the HRESULT form of <c>ERROR_INVALID_PARAMETER</c>.
    /// </returns>
    /// <remarks>
    /// Correlation vectors are optional telemetry state. Providers commonly retrieve the vector
    /// assigned by Windows, increment its final clock component, and write the new value back.
    /// Calls that coordinate updates to one file require caller synchronization.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfSetCorrelationVector))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfSetCorrelationVector(
        nint fileHandle,
        CfCorrelationVector* correlationVector);

    /// <summary>Retrieves the correlation vector currently associated with a placeholder.</summary>
    /// <param name="fileHandle">
    /// Open Win32 handle with read-data or write-DAC access to the placeholder.
    /// </param>
    /// <param name="correlationVector">Pointer to caller-owned storage receiving the vector.</param>
    /// <returns>The native <c>HRESULT</c> without translation.</returns>
    /// <remarks>
    /// Windows can assign the initial vector when the file is first opened. A successful query can
    /// return a zero-initialized vector before assignment; callers must inspect
    /// <see cref="CfCorrelationVector.Version"/>. The returned structure owns no external memory
    /// and can be copied after the function returns.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetCorrelationVector))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial int CfGetCorrelationVector(
        nint fileHandle,
        CfCorrelationVector* correlationVector);

    /// <summary>Infers Cloud Files state from Win32 file attributes and a reparse tag.</summary>
    /// <param name="fileAttributes">Win32 file attribute bits.</param>
    /// <param name="reparseTag">Win32 reparse tag for the item.</param>
    /// <returns>
    /// A set of placeholder-state flags, or <see cref="CfPlaceholderState.Invalid"/> when the
    /// supplied values cannot be interpreted.
    /// </returns>
    /// <remarks>
    /// This function performs no I/O and returns state directly rather than an <c>HRESULT</c>.
    /// The inputs can be obtained from file attribute-tag information or directory find data.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetPlaceholderStateFromAttributeTag), SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    public static partial CfPlaceholderState CfGetPlaceholderStateFromAttributeTag(
        uint fileAttributes,
        uint reparseTag);

    /// <summary>Infers Cloud Files state from a buffer returned by a Win32 file-information query.</summary>
    /// <param name="infoBuffer">
    /// Pointer to file information returned by <c>GetFileInformationByHandleEx</c>.
    /// </param>
    /// <param name="infoClass">
    /// Numeric Win32 <c>FILE_INFO_BY_HANDLE_CLASS</c> value describing
    /// <paramref name="infoBuffer"/>. Unsupported classes return invalid state and set last error.
    /// </param>
    /// <returns>
    /// A set of placeholder-state flags, or <see cref="CfPlaceholderState.Invalid"/> on failure.
    /// </returns>
    /// <remarks>The input buffer is borrowed for the duration of the call and is never retained.</remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetPlaceholderStateFromFileInfo), SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the generic Win32 information buffer.")]
    public static unsafe partial CfPlaceholderState CfGetPlaceholderStateFromFileInfo(
        void* infoBuffer,
        int infoClass);

    /// <summary>Infers Cloud Files state from Unicode Win32 directory find data.</summary>
    /// <param name="findData">
    /// Pointer to caller-owned <see cref="CfWin32FindData"/> obtained from a Unicode Win32 find
    /// operation. The pointer is borrowed only for this call.
    /// </param>
    /// <returns>
    /// A set of placeholder-state flags, or <see cref="CfPlaceholderState.Invalid"/> on failure.
    /// </returns>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetPlaceholderStateFromFindData), SetLastError = true)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native pointer contract.")]
    public static unsafe partial CfPlaceholderState CfGetPlaceholderStateFromFindData(
        CfWin32FindData* findData);

    /// <summary>Retrieves state, identifiers, storage accounting, and identity for a placeholder.</summary>
    /// <param name="fileHandle">Open Win32 handle requiring only read-attributes access.</param>
    /// <param name="infoClass">Selects the basic or standard variable-length result.</param>
    /// <param name="infoBuffer">Pointer to caller-owned writable result storage.</param>
    /// <param name="infoBufferLength">Capacity of <paramref name="infoBuffer"/> in bytes.</param>
    /// <param name="returnedLength">
    /// Optional pointer receiving the number of bytes written or required by the result.
    /// </param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. If the buffer is too small, Windows can
    /// return a partial result together with the HRESULT form of <c>ERROR_MORE_DATA</c>.
    /// </returns>
    /// <remarks>
    /// The fixed structure selected by <paramref name="infoClass"/> ends with a variable-length
    /// identity. The function is observational, retains no memory, and may execute concurrently
    /// with other read-only queries subject to ordinary handle lifetime rules.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetPlaceholderInfo))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the variable-length native result.")]
    public static unsafe partial int CfGetPlaceholderInfo(
        nint fileHandle,
        CfPlaceholderInfoClass infoClass,
        void* infoBuffer,
        uint infoBufferLength,
        uint* returnedLength);

    /// <summary>Retrieves on-disk, validated, or modified byte ranges for a placeholder.</summary>
    /// <param name="fileHandle">Open Win32 handle requiring only read-attributes access.</param>
    /// <param name="infoClass">Category of ranges to return.</param>
    /// <param name="startingOffset">First byte included in the query.</param>
    /// <param name="length">
    /// Query length in bytes, or <see cref="EndOfFile"/> to query through logical end of file.
    /// </param>
    /// <param name="infoBuffer">Pointer to writable storage for an array of <see cref="CfFileRange"/>.</param>
    /// <param name="infoBufferLength">Capacity of <paramref name="infoBuffer"/> in bytes.</param>
    /// <param name="returnedLength">Optional pointer receiving the number of bytes written.</param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. The HRESULT forms of
    /// <c>ERROR_MORE_DATA</c> and <c>ERROR_HANDLE_EOF</c> are part of normal range enumeration.
    /// </returns>
    /// <remarks>The function is observational and retains no caller-owned memory.</remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetPlaceholderRangeInfo))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the variable-length native result.")]
    public static unsafe partial int CfGetPlaceholderRangeInfo(
        nint fileHandle,
        CfPlaceholderRangeInfoClass infoClass,
        long startingOffset,
        long length,
        void* infoBuffer,
        uint infoBufferLength,
        uint* returnedLength);

    /// <summary>Retrieves placeholder ranges by callback identity without opening the file.</summary>
    /// <param name="connectionKey">Provider connection from the callback.</param>
    /// <param name="transferKey">Transfer stream from the callback or <see cref="CfGetTransferKey"/>.</param>
    /// <param name="fileId">Volume-wide file identifier supplied with the callback.</param>
    /// <param name="infoClass">Category of ranges to return.</param>
    /// <param name="startingOffset">First byte included in the query.</param>
    /// <param name="rangeLength">
    /// Query length in bytes, or <see cref="EndOfFile"/> to query through logical end of file.
    /// </param>
    /// <param name="infoBuffer">Pointer to writable storage for an array of <see cref="CfFileRange"/>.</param>
    /// <param name="infoBufferSize">Capacity of <paramref name="infoBuffer"/> in bytes.</param>
    /// <param name="infoBufferWritten">Optional pointer receiving the number of bytes written.</param>
    /// <returns>The native <c>HRESULT</c> without translation.</returns>
    /// <remarks>
    /// This path bypasses file-system filter stacks that could deadlock a fetch callback. Before
    /// calling it, verify that <see cref="CfPlatformInfo.IntegrationNumber"/> is at least
    /// <c>0x600</c>. For queries outside a callback, prefer <see cref="CfGetPlaceholderRangeInfo"/>.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetPlaceholderRangeInfoForHydration))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the callback-key native contract.")]
    public static unsafe partial int CfGetPlaceholderRangeInfoForHydration(
        CfConnectionKey connectionKey,
        CfTransferKey transferKey,
        long fileId,
        CfPlaceholderRangeInfoClass infoClass,
        long startingOffset,
        long rangeLength,
        void* infoBuffer,
        uint infoBufferSize,
        uint* infoBufferWritten);

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

    /// <summary>Acquires a transfer key for provider-initiated operations on a placeholder.</summary>
    /// <param name="fileHandle">
    /// Open Win32 handle to a placeholder with read-data or write-DAC access. The caller retains
    /// ownership and must keep it open while the returned key is used.
    /// </param>
    /// <param name="transferKey">Receives the opaque transfer key.</param>
    /// <returns>
    /// The native <c>HRESULT</c> without translation. A value of zero is <c>S_OK</c>;
    /// negative values indicate failure.
    /// </returns>
    /// <remarks>
    /// A successful acquisition must be paired with <see cref="CfReleaseTransferKey"/> using the
    /// same open file handle and key. The key may be supplied to <see cref="CfExecute"/> to drive
    /// proactive data transfer outside a fetch callback. Callers must synchronize handle closure
    /// with key use.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfGetTransferKey))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native handle contract.")]
    public static unsafe partial int CfGetTransferKey(
        nint fileHandle,
        CfTransferKey* transferKey);

    /// <summary>Releases a transfer key obtained by <see cref="CfGetTransferKey"/>.</summary>
    /// <param name="fileHandle">The same still-open Win32 handle used to acquire the key.</param>
    /// <param name="transferKey">Pointer to the transfer key being released.</param>
    /// <remarks>
    /// The call has no return value and retains no memory. It does not close
    /// <paramref name="fileHandle"/>. The caller must prevent concurrent handle closure.
    /// </remarks>
    [LibraryImport("CldApi.dll", EntryPoint = nameof(CfReleaseTransferKey))]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    [SupportedOSPlatform("windows10.0.16299")]
    [SuppressMessage(
        "Interoperability",
        "CA1401:P/Invokes should not be visible",
        Justification = "CfSharp.Native intentionally exposes the complete native handle contract.")]
    public static unsafe partial void CfReleaseTransferKey(
        nint fileHandle,
        CfTransferKey* transferKey);

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
