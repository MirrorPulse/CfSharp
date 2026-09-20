using System.Runtime.InteropServices;

namespace CfSharp.Native;

/// <summary>Identifies a callback operation completed through <see cref="CfApi.CfExecute"/>.</summary>
/// <remarks>
/// Every pointer is borrowed caller memory and need remain valid only until
/// <see cref="CfApi.CfExecute"/> returns. Keys must originate from the callback being completed.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CfOperationInfo
{
    /// <summary>Size of this structure in bytes.</summary>
    public uint StructSize;

    /// <summary>Operation type selecting the active parameter branch.</summary>
    public CfOperationType Type;

    /// <summary>Connection on which the request was received.</summary>
    public CfConnectionKey ConnectionKey;

    /// <summary>Transfer stream associated with the request.</summary>
    public CfTransferKey TransferKey;

    /// <summary>Pointer to an optional native correlation vector.</summary>
    public void* CorrelationVector;

    /// <summary>Pointer to optional provider status details.</summary>
    public CfSyncStatus* SyncStatus;

    /// <summary>Request identifier supplied by Windows.</summary>
    public CfRequestKey RequestKey;
}
