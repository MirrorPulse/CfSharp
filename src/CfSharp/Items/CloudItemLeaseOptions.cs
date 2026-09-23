namespace CfSharp;

/// <summary>Controls the access and oplock behavior of a cloud item lease.</summary>
/// <remarks>
/// The options are validated before Windows is called. A lease never exposes the underlying
/// protected handle; these values only select the access flags used while opening it.
/// </remarks>
public sealed record CloudItemLeaseOptions
{
    /// <summary>Gets read-only, shared access with the normal read-caching oplock.</summary>
    public static CloudItemLeaseOptions ReadOnly { get; } = new();

    /// <summary>Gets exclusive foreground write access.</summary>
    public static CloudItemLeaseOptions ExclusiveWrite { get; } = new()
    {
        Access = CloudItemLeaseAccess.Read | CloudItemLeaseAccess.Write,
        Exclusive = true,
    };

    /// <summary>Gets the requested read/write access.</summary>
    public CloudItemLeaseAccess Access { get; init; } = CloudItemLeaseAccess.Read;

    /// <summary>Gets whether Windows should request an exclusive oplock.</summary>
    public bool Exclusive { get; init; }

    /// <summary>Gets whether Windows should open the item as a foreground caller.</summary>
    public bool Foreground { get; init; }

    /// <summary>Gets whether the native open should request delete access.</summary>
    public bool DeleteAccess { get; init; }

    internal void Validate()
    {
        const CloudItemLeaseAccess supported = CloudItemLeaseAccess.Read | CloudItemLeaseAccess.Write;
        if (Access == CloudItemLeaseAccess.None || (Access & ~supported) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Access),
                Access,
                "Lease access must include read or write and contain no unknown flags.");
        }

        if (Foreground && Exclusive)
        {
            throw new ArgumentException(
                "Foreground access breaks existing oplocks and cannot request an exclusive oplock.",
                nameof(Foreground));
        }
    }
}

/// <summary>Flags the managed access requested for a cloud item lease.</summary>
[Flags]
public enum CloudItemLeaseAccess
{
    /// <summary>No access. This value is invalid for a lease.</summary>
    None = 0,

    /// <summary>Read-data access.</summary>
    Read = 1,

    /// <summary>Write-data or directory add-file access.</summary>
    Write = 2,
}
