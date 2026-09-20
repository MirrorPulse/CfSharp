using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>Describes a content-validation callback.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors cfapi.h.")]
public enum CfCallbackValidateDataFlags : uint
{
    /// <summary>The request has no additional characteristics.</summary>
    None = 0,

    /// <summary>The validation follows an explicit hydration operation.</summary>
    ExplicitHydration = 0x00000002,
}

/// <summary>Describes a placeholder-enumeration callback.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors cfapi.h.")]
public enum CfCallbackFetchPlaceholdersFlags : uint
{
    /// <summary>The request has no additional characteristics.</summary>
    None = 0,
}

/// <summary>Describes completion of a placeholder open operation.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors cfapi.h.")]
public enum CfCallbackOpenCompletionFlags : uint
{
    /// <summary>The open completed without an additional placeholder classification.</summary>
    None = 0,

    /// <summary>The opened item has an unknown placeholder state.</summary>
    PlaceholderUnknown = 0x00000001,

    /// <summary>The opened item uses an unsupported placeholder version.</summary>
    PlaceholderUnsupported = 0x00000002,
}

/// <summary>Describes completion of a placeholder close operation.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors cfapi.h.")]
public enum CfCallbackCloseCompletionFlags : uint
{
    /// <summary>The close has no additional characteristics.</summary>
    None = 0,

    /// <summary>The placeholder was deleted before its final handle closed.</summary>
    Deleted = 0x00000001,
}

/// <summary>Describes a pre-dehydration notification.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors cfapi.h.")]
public enum CfCallbackDehydrateFlags : uint
{
    /// <summary>The dehydration has no additional characteristics.</summary>
    None = 0,

    /// <summary>The dehydration is running as a background operation.</summary>
    Background = 0x00000001,
}

/// <summary>Describes completion of a dehydration operation.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors cfapi.h.")]
public enum CfCallbackDehydrateCompletionFlags : uint
{
    /// <summary>The completion has no additional characteristics.</summary>
    None = 0,

    /// <summary>The dehydration ran as a background operation.</summary>
    Background = 0x00000001,

    /// <summary>The placeholder content was successfully dehydrated.</summary>
    Dehydrated = 0x00000002,
}

/// <summary>Describes a pre-delete notification.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors cfapi.h.")]
public enum CfCallbackDeleteFlags : uint
{
    /// <summary>The delete has no additional characteristics.</summary>
    None = 0,

    /// <summary>The target is a directory.</summary>
    IsDirectory = 0x00000001,

    /// <summary>The operation restores a previously deleted placeholder.</summary>
    IsUndelete = 0x00000002,
}

/// <summary>Describes completion of a delete operation.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors cfapi.h.")]
public enum CfCallbackDeleteCompletionFlags : uint
{
    /// <summary>The completion has no additional characteristics.</summary>
    None = 0,
}

/// <summary>Describes a pre-rename notification.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors cfapi.h.")]
public enum CfCallbackRenameFlags : uint
{
    /// <summary>The rename has no additional characteristics.</summary>
    None = 0,

    /// <summary>The source item is a directory.</summary>
    IsDirectory = 0x00000001,

    /// <summary>The source path is inside the connected sync root.</summary>
    SourceInScope = 0x00000002,

    /// <summary>The target path is inside the connected sync root.</summary>
    TargetInScope = 0x00000004,
}

/// <summary>Describes completion of a rename operation.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors cfapi.h.")]
public enum CfCallbackRenameCompletionFlags : uint
{
    /// <summary>The completion has no additional characteristics.</summary>
    None = 0,
}
