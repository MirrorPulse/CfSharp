using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Provides common context for every Cloud Files callback.
/// </summary>
/// <remarks>
/// <para>
/// This is the blittable representation of <c>CF_CALLBACK_INFO</c>. All pointers and identity
/// buffers are borrowed from Windows and remain valid only until the callback returns. Copy any
/// required data before returning and never retain a pointer into this structure.
/// </para>
/// <para>
/// The same connection may invoke callbacks concurrently. The provider is responsible for
/// synchronizing access to state referenced by <see cref="CallbackContext"/>.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfCallbackInfo
{
    /// <summary>Size of this callback-information structure in bytes.</summary>
    public uint StructSize;

    /// <summary>Connection on which the callback was delivered.</summary>
    public CfConnectionKey ConnectionKey;

    /// <summary>Provider context pointer supplied when the connection was created.</summary>
    public void* CallbackContext;

    /// <summary>Pointer to the null-terminated UTF-16 volume GUID name.</summary>
    public char* VolumeGuidName;

    /// <summary>Pointer to the null-terminated UTF-16 DOS volume name.</summary>
    public char* VolumeDosName;

    /// <summary>Serial number of the containing volume.</summary>
    public uint VolumeSerialNumber;

    /// <summary>Volume-specific file identifier of the sync root.</summary>
    public long SyncRootFileId;

    /// <summary>Pointer to the provider-defined sync-root identity.</summary>
    public void* SyncRootIdentity;

    /// <summary>Length of <see cref="SyncRootIdentity"/> in bytes.</summary>
    public uint SyncRootIdentityLength;

    /// <summary>Volume-specific identifier of the callback target.</summary>
    public long FileId;

    /// <summary>Logical size of the callback target file in bytes.</summary>
    public long FileSize;

    /// <summary>Pointer to the provider-defined target file identity.</summary>
    public void* FileIdentity;

    /// <summary>Length of <see cref="FileIdentity"/> in bytes.</summary>
    public uint FileIdentityLength;

    /// <summary>
    /// Pointer to the null-terminated normalized UTF-16 target path. Whether this path is full
    /// or root-relative depends on <see cref="CfConnectFlags.RequireFullFilePath"/>.
    /// </summary>
    public char* NormalizedPath;

    /// <summary>Opaque transfer key used to complete or manipulate the callback request.</summary>
    public CfTransferKey TransferKey;

    /// <summary>Relative priority hint from zero through <see cref="CfApi.MaxPriorityHint"/>.</summary>
    public byte PriorityHint;

    /// <summary>Pointer to a borrowed native correlation vector, when present.</summary>
    public void* CorrelationVector;

    /// <summary>Pointer to borrowed process information, when requested and available.</summary>
    public CfProcessInfo* ProcessInfo;

    /// <summary>Opaque request key used by request-specific operations.</summary>
    public CfRequestKey RequestKey;
}
