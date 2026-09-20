using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>
/// Represents the native union containing callback-specific parameters.
/// </summary>
/// <remarks>
/// <para>
/// Read only the field selected by the <see cref="CfCallbackType"/> used to register the
/// current callback. The structure and every pointer reachable from it are borrowed callback
/// memory and are valid only until the callback returns.
/// </para>
/// <para>
/// The explicit size and offsets are part of the Windows ABI. <see cref="ParamSize"/> reports
/// the versioned size supplied by Windows and should be checked before reading fields added by
/// later platform versions.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct CfCallbackParameters
{
    /// <summary>Size of the populated callback-parameters structure in bytes.</summary>
    [FieldOffset(0)]
    public uint ParamSize;

    /// <summary>Parameters for content-fetch cancellation callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackCancelParameters Cancel;

    /// <summary>Parameters for file-content fetch callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackFetchDataParameters FetchData;

    /// <summary>Parameters for content-validation callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackValidateDataParameters ValidateData;

    /// <summary>Parameters for placeholder-enumeration callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackFetchPlaceholdersParameters FetchPlaceholders;

    /// <summary>Parameters for file-open completion callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackOpenCompletionParameters OpenCompletion;

    /// <summary>Parameters for file-close completion callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackCloseCompletionParameters CloseCompletion;

    /// <summary>Parameters for pre-dehydration callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackDehydrateParameters Dehydrate;

    /// <summary>Parameters for dehydration-completion callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackDehydrateCompletionParameters DehydrateCompletion;

    /// <summary>Parameters for pre-delete callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackDeleteParameters Delete;

    /// <summary>Parameters for delete-completion callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackDeleteCompletionParameters DeleteCompletion;

    /// <summary>Parameters for pre-rename callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackRenameParameters Rename;

    /// <summary>Parameters for rename-completion callbacks.</summary>
    [FieldOffset(8)]
    public CfCallbackRenameCompletionParameters RenameCompletion;
}

/// <summary>Contains cancellation details for a content or placeholder request.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfCallbackCancelParameters
{
    /// <summary>Reason flags for the cancellation.</summary>
    public CfCallbackCancelFlags Flags;

    /// <summary>Starting file offset of the cancelled content request.</summary>
    public long FileOffset;

    /// <summary>Length in bytes of the cancelled content request.</summary>
    public long Length;
}

/// <summary>Contains requested and optional ranges for a file-content fetch.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfCallbackFetchDataParameters
{
    /// <summary>Characteristics of the fetch request.</summary>
    public CfCallbackFetchDataFlags Flags;

    /// <summary>Starting offset of the range required to complete user I/O.</summary>
    public long RequiredFileOffset;

    /// <summary>Length in bytes of the required range.</summary>
    public long RequiredLength;

    /// <summary>Starting offset of an additional range Windows permits the provider to transfer.</summary>
    public long OptionalFileOffset;

    /// <summary>Length in bytes of the optional range.</summary>
    public long OptionalLength;

    /// <summary>Native file time of the most recent dehydration, or zero when unavailable.</summary>
    public long LastDehydrationTime;

    /// <summary>Reason for the most recent dehydration.</summary>
    public CfCallbackDehydrationReason LastDehydrationReason;
}

/// <summary>Contains the range whose hydrated content Windows asks the provider to validate.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfCallbackValidateDataParameters
{
    /// <summary>Characteristics of the validation request.</summary>
    public CfCallbackValidateDataFlags Flags;

    /// <summary>Starting file offset of the range to validate.</summary>
    public long RequiredFileOffset;

    /// <summary>Length in bytes of the range to validate.</summary>
    public long RequiredLength;
}

/// <summary>Contains the search pattern for a placeholder-enumeration request.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfCallbackFetchPlaceholdersParameters
{
    /// <summary>Characteristics of the enumeration request.</summary>
    public CfCallbackFetchPlaceholdersFlags Flags;

    /// <summary>Pointer to a borrowed null-terminated UTF-16 search pattern.</summary>
    public char* Pattern;
}

/// <summary>Contains the result classification of a completed placeholder open.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfCallbackOpenCompletionParameters
{
    /// <summary>Result classification flags.</summary>
    public CfCallbackOpenCompletionFlags Flags;
}

/// <summary>Contains details of a completed placeholder close.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfCallbackCloseCompletionParameters
{
    /// <summary>Close-completion flags.</summary>
    public CfCallbackCloseCompletionFlags Flags;
}

/// <summary>Contains details of a requested placeholder dehydration.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfCallbackDehydrateParameters
{
    /// <summary>Characteristics of the dehydration request.</summary>
    public CfCallbackDehydrateFlags Flags;

    /// <summary>Reason Windows requested dehydration.</summary>
    public CfCallbackDehydrationReason Reason;
}

/// <summary>Contains details of a completed placeholder dehydration.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfCallbackDehydrateCompletionParameters
{
    /// <summary>Dehydration-completion flags.</summary>
    public CfCallbackDehydrateCompletionFlags Flags;

    /// <summary>Reason associated with the dehydration.</summary>
    public CfCallbackDehydrationReason Reason;
}

/// <summary>Contains details of a requested placeholder deletion.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfCallbackDeleteParameters
{
    /// <summary>Characteristics of the delete request.</summary>
    public CfCallbackDeleteFlags Flags;
}

/// <summary>Contains details of a completed placeholder deletion.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CfCallbackDeleteCompletionParameters
{
    /// <summary>Delete-completion flags.</summary>
    public CfCallbackDeleteCompletionFlags Flags;
}

/// <summary>Contains the target path of a requested placeholder rename or move.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfCallbackRenameParameters
{
    /// <summary>Characteristics and scope of the rename.</summary>
    public CfCallbackRenameFlags Flags;

    /// <summary>Pointer to the borrowed null-terminated UTF-16 target path.</summary>
    public char* TargetPath;
}

/// <summary>Contains the source path of a completed placeholder rename or move.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfCallbackRenameCompletionParameters
{
    /// <summary>Rename-completion flags.</summary>
    public CfCallbackRenameCompletionFlags Flags;

    /// <summary>Pointer to the borrowed null-terminated UTF-16 source path.</summary>
    public char* SourcePath;
}
