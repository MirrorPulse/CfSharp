namespace CfSharp.Native;

/// <summary>Selects the active branch of <see cref="CfOperationParameters"/>.</summary>
public enum CfOperationType
{
    /// <summary>Supplies file content for a hydration request.</summary>
    TransferData = 0,

    /// <summary>Retrieves content previously supplied to a placeholder.</summary>
    RetrieveData = 1,

    /// <summary>Acknowledges validation of hydrated content.</summary>
    AckData = 2,

    /// <summary>Restarts hydration after updating metadata or identity.</summary>
    RestartHydration = 3,

    /// <summary>Supplies child placeholders for an enumeration request.</summary>
    TransferPlaceholders = 4,

    /// <summary>Acknowledges or rejects a dehydration request.</summary>
    AckDehydrate = 5,

    /// <summary>Acknowledges or rejects a delete request.</summary>
    AckDelete = 6,

    /// <summary>Acknowledges or rejects a rename request.</summary>
    AckRename = 7,
}
