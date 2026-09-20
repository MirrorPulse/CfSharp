namespace CfSharp.Native;

/// <summary>
/// Describes the activity or terminal state reported by a sync provider.
/// </summary>
/// <remarks>
/// Activity values are flags and may be combined. <see cref="Terminated"/> and
/// <see cref="Error"/> are terminal status values defined by the native API.
/// </remarks>
[Flags]
public enum CfSyncProviderStatus : uint
{
    /// <summary>The provider is not connected to the sync root.</summary>
    Disconnected = 0x00000000,

    /// <summary>The provider is connected but has no active synchronization work.</summary>
    Idle = 0x00000001,

    /// <summary>The provider is populating placeholder namespace entries.</summary>
    PopulateNamespace = 0x00000002,

    /// <summary>The provider is populating placeholder metadata.</summary>
    PopulateMetadata = 0x00000004,

    /// <summary>The provider is populating file content.</summary>
    PopulateContent = 0x00000008,

    /// <summary>The provider is performing incremental synchronization.</summary>
    SyncIncremental = 0x00000010,

    /// <summary>The provider is performing full synchronization.</summary>
    SyncFull = 0x00000020,

    /// <summary>The provider has lost connectivity to its backing service.</summary>
    ConnectivityLost = 0x00000040,

    /// <summary>Clears previously reported activity flags.</summary>
    ClearFlags = 0x80000000,

    /// <summary>The provider terminated and can no longer service the sync root.</summary>
    Terminated = 0xC0000001,

    /// <summary>The provider encountered an error that prevents normal operation.</summary>
    Error = 0xC0000002,
}
