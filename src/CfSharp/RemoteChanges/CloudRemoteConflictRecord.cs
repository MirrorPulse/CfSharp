namespace CfSharp;

/// <summary>Associates a decoded unresolved remote conflict with its durable identifier and owning root.</summary>
/// <remarks>
/// Immutable and safe for concurrent reads; owns no native handles or state-store resources.
/// The root routes the identifier to the current CloudFileSystem instance, not a globally stable
/// application account. Applications should retain their own instance mapping if a root moves.
/// </remarks>
public sealed class CloudRemoteConflictRecord
{
    internal CloudRemoteConflictRecord(Guid conflictId, string syncRootPath, CloudRemoteConflict conflict)
    {
        ConflictId = conflictId;
        SyncRootPath = syncRootPath;
        Conflict = conflict;
    }

    /// <summary>Gets the stable identifier accepted by remote conflict resolution APIs.</summary>
    public Guid ConflictId { get; }

    /// <summary>Gets the owning instance's normalized absolute sync-root path at query time.</summary>
    public string SyncRootPath { get; }

    /// <summary>Gets the decoded conflict; its local state is the query-time state, not historical state.</summary>
    public CloudRemoteConflict Conflict { get; }
}
