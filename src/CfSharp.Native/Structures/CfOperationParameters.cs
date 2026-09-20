using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Represents the native union containing parameters for <see cref="CfApi.CfExecute"/>.</summary>
/// <remarks>
/// Initialize only the field selected by <see cref="CfOperationInfo.Type"/>. Set
/// <see cref="ParamSize"/> to the offset of that field plus the native size of the selected
/// branch. Buffer pointers remain caller-owned and must stay valid until the call returns.
/// </remarks>
[StructLayout(LayoutKind.Explicit)]
public unsafe struct CfOperationParameters
{
    /// <summary>Versioned size of the populated structure in bytes.</summary>
    [FieldOffset(0)]
    public uint ParamSize;

    /// <summary>Parameters for supplying hydrated file data.</summary>
    [FieldOffset(8)]
    public CfOperationTransferDataParameters TransferData;

    /// <summary>Parameters for retrieving previously supplied data.</summary>
    [FieldOffset(8)]
    public CfOperationRetrieveDataParameters RetrieveData;

    /// <summary>Parameters for acknowledging validated data.</summary>
    [FieldOffset(8)]
    public CfOperationAckDataParameters AckData;

    /// <summary>Parameters for restarting hydration.</summary>
    [FieldOffset(8)]
    public CfOperationRestartHydrationParameters RestartHydration;

    /// <summary>Parameters for supplying child placeholders.</summary>
    [FieldOffset(8)]
    public CfOperationTransferPlaceholdersParameters TransferPlaceholders;

    /// <summary>Parameters for acknowledging a dehydration request.</summary>
    [FieldOffset(8)]
    public CfOperationAckDehydrateParameters AckDehydrate;

    /// <summary>Parameters for acknowledging a rename request.</summary>
    [FieldOffset(8)]
    public CfOperationAckRenameParameters AckRename;

    /// <summary>Parameters for acknowledging a delete request.</summary>
    [FieldOffset(8)]
    public CfOperationAckDeleteParameters AckDelete;
}

/// <summary>Supplies a range of file content or completes a transfer with a failure.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfOperationTransferDataParameters
{
    /// <summary>Transfer behavior flags.</summary>
    public CfOperationTransferDataFlags Flags;

    /// <summary>Successful or Cloud Files-specific terminal status.</summary>
    public NtStatus CompletionStatus;

    /// <summary>Pointer to at least <see cref="Length"/> bytes when completion succeeds.</summary>
    public void* Buffer;

    /// <summary>Destination offset in the placeholder file.</summary>
    public long Offset;

    /// <summary>Number of bytes transferred or covered by the failure.</summary>
    public long Length;
}

/// <summary>Retrieves content from a hydrated placeholder range into a writable buffer.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfOperationRetrieveDataParameters
{
    /// <summary>Retrieval behavior flags.</summary>
    public CfOperationRetrieveDataFlags Flags;

    /// <summary>Pointer to caller-owned writable storage.</summary>
    public void* Buffer;

    /// <summary>Source offset in the placeholder file.</summary>
    public long Offset;

    /// <summary>Capacity of <see cref="Buffer"/> in bytes.</summary>
    public long Length;

    /// <summary>Receives the number of bytes retrieved.</summary>
    public long ReturnedLength;
}

/// <summary>Acknowledges validation of a file-content range.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfOperationAckDataParameters
{
    /// <summary>Acknowledgement behavior flags.</summary>
    public CfOperationAckDataFlags Flags;

    /// <summary>Successful or Cloud Files-specific terminal status.</summary>
    public NtStatus CompletionStatus;

    /// <summary>Starting offset of the validated range.</summary>
    public long Offset;

    /// <summary>Length of the validated range in bytes.</summary>
    public long Length;
}

/// <summary>Updates placeholder metadata or identity before restarting hydration.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfOperationRestartHydrationParameters
{
    /// <summary>Restart behavior flags.</summary>
    public CfOperationRestartHydrationFlags Flags;

    /// <summary>Pointer to optional replacement file-system metadata.</summary>
    public CfFsMetadata* FsMetadata;

    /// <summary>Pointer to an optional replacement file identity.</summary>
    public void* FileIdentity;

    /// <summary>Length of <see cref="FileIdentity"/> in bytes.</summary>
    public uint FileIdentityLength;
}

/// <summary>Supplies placeholder entries for a directory population request.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfOperationTransferPlaceholdersParameters
{
    /// <summary>Transfer behavior flags.</summary>
    public CfOperationTransferPlaceholdersFlags Flags;

    /// <summary>Successful or Cloud Files-specific terminal status.</summary>
    public NtStatus CompletionStatus;

    /// <summary>Total number of children in the provider namespace, when known.</summary>
    public long PlaceholderTotalCount;

    /// <summary>Pointer to caller-owned placeholder entries.</summary>
    public CfPlaceholderCreateInfo* PlaceholderArray;

    /// <summary>Number of entries in <see cref="PlaceholderArray"/>.</summary>
    public uint PlaceholderCount;

    /// <summary>Receives the number of entries processed by Windows.</summary>
    public uint EntriesProcessed;
}

/// <summary>Acknowledges or rejects a dehydration request.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfOperationAckDehydrateParameters
{
    /// <summary>Acknowledgement behavior flags.</summary>
    public CfOperationAckDehydrateFlags Flags;

    /// <summary>Successful or Cloud Files-specific terminal status.</summary>
    public NtStatus CompletionStatus;

    /// <summary>Pointer to an optional replacement file identity.</summary>
    public void* FileIdentity;

    /// <summary>Length of <see cref="FileIdentity"/> in bytes.</summary>
    public uint FileIdentityLength;
}

/// <summary>Acknowledges or rejects a rename request.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfOperationAckRenameParameters
{
    /// <summary>Acknowledgement behavior flags.</summary>
    public CfOperationAckRenameFlags Flags;

    /// <summary>Successful or Cloud Files-specific terminal status.</summary>
    public NtStatus CompletionStatus;
}

/// <summary>Acknowledges or rejects a delete request.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfOperationAckDeleteParameters
{
    /// <summary>Acknowledgement behavior flags.</summary>
    public CfOperationAckDeleteFlags Flags;

    /// <summary>Successful or Cloud Files-specific terminal status.</summary>
    public NtStatus CompletionStatus;
}
