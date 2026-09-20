namespace CfSharp;

/// <summary>
/// Describes the current activity or terminal state of a sync provider.
/// </summary>
[Flags]
public enum CloudProviderStatus : uint
{
    /// <summary>The provider is not connected.</summary>
    Disconnected = 0x00000000,

    /// <summary>The provider is connected without active synchronization work.</summary>
    Idle = 0x00000001,

    /// <summary>The provider is populating namespace entries.</summary>
    PopulateNamespace = 0x00000002,

    /// <summary>The provider is populating metadata.</summary>
    PopulateMetadata = 0x00000004,

    /// <summary>The provider is populating file content.</summary>
    PopulateContent = 0x00000008,

    /// <summary>The provider is performing incremental synchronization.</summary>
    SyncIncremental = 0x00000010,

    /// <summary>The provider is performing full synchronization.</summary>
    SyncFull = 0x00000020,

    /// <summary>The provider has lost connectivity to its backing service.</summary>
    ConnectivityLost = 0x00000040,

    /// <summary>Represents the native request to clear previously reported activity flags.</summary>
    ClearFlags = 0x80000000,

    /// <summary>The provider terminated and can no longer service the sync root.</summary>
    Terminated = 0xC0000001,

    /// <summary>The provider encountered an error that prevents normal operation.</summary>
    Error = 0xC0000002,
}
