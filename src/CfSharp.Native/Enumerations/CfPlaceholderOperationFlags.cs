using System.Diagnostics.CodeAnalysis;

namespace CfSharp.Native;

/// <summary>Controls conversion of an ordinary file or directory into a Cloud Files placeholder.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_CONVERT_FLAGS.")]
public enum CfConvertFlags : uint
{
    /// <summary>Uses the default conversion behavior.</summary>
    None = 0,

    /// <summary>Marks the converted placeholder as synchronized with its provider state.</summary>
    MarkInSync = 0x00000001,

    /// <summary>Dehydrates a converted file. The caller must hold an exclusive handle.</summary>
    Dehydrate = 0x00000002,

    /// <summary>Enables on-demand population for a converted directory.</summary>
    EnableOnDemandPopulation = 0x00000004,

    /// <summary>Marks a converted file as always fully present.</summary>
    AlwaysFull = 0x00000008,

    /// <summary>Allows conversion from another placeholder implementation to Cloud Files.</summary>
    ForceConvertToCloudFile = 0x00000010,
}

/// <summary>Controls an update to an existing placeholder.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_UPDATE_FLAGS.")]
public enum CfUpdateFlags : uint
{
    /// <summary>Uses the default update behavior.</summary>
    None = 0,

    /// <summary>Fails the update unless the placeholder is currently in sync.</summary>
    VerifyInSync = 0x00000001,

    /// <summary>Marks the placeholder as in sync after a successful update.</summary>
    MarkInSync = 0x00000002,

    /// <summary>Dehydrates the complete file after updating it.</summary>
    Dehydrate = 0x00000004,

    /// <summary>Enables on-demand population for a directory.</summary>
    EnableOnDemandPopulation = 0x00000008,

    /// <summary>Disables on-demand population for a directory.</summary>
    DisableOnDemandPopulation = 0x00000010,

    /// <summary>Removes the placeholder file identity.</summary>
    RemoveFileIdentity = 0x00000020,

    /// <summary>Marks the placeholder as not in sync after a successful update.</summary>
    ClearInSync = 0x00000040,

    /// <summary>Removes all extrinsic placeholder properties.</summary>
    RemoveProperty = 0x00000080,

    /// <summary>Passes zero-valued metadata fields through instead of treating them as unchanged.</summary>
    PassthroughFsMetadata = 0x00000100,

    /// <summary>Marks the file as always fully present.</summary>
    AlwaysFull = 0x00000200,

    /// <summary>Clears the always-full state so the file can be dehydrated.</summary>
    AllowPartial = 0x00000400,
}

/// <summary>Controls conversion of a placeholder back to an ordinary file or directory.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_REVERT_FLAGS.")]
public enum CfRevertFlags : uint
{
    /// <summary>Uses the only behavior currently defined by the platform.</summary>
    None = 0,
}

/// <summary>Controls explicit hydration of a placeholder range.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_HYDRATE_FLAGS.")]
public enum CfHydrateFlags : uint
{
    /// <summary>Uses the only behavior currently defined by the platform.</summary>
    None = 0,
}

/// <summary>Controls explicit dehydration of a placeholder range.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Mirrors CF_DEHYDRATE_FLAGS.")]
public enum CfDehydrateFlags : uint
{
    /// <summary>Runs dehydration as a foreground operation.</summary>
    None = 0,

    /// <summary>Identifies a background dehydration operation.</summary>
    Background = 0x00000001,
}
