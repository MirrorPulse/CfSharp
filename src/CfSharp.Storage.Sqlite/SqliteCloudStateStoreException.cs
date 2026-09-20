using Microsoft.Data.Sqlite;

namespace CfSharp.Storage.Sqlite;

/// <summary>Classifies failures produced by the official SQLite state provider.</summary>
public enum SqliteCloudStateStoreError
{
    /// <summary>The configured database path is invalid or unsafe.</summary>
    InvalidPath = 0,

    /// <summary>The database would reside inside the managed sync root.</summary>
    PathInsideSyncRoot = 1,

    /// <summary>Another process or file-system instance already owns the database.</summary>
    AlreadyInUse = 2,

    /// <summary>The file is not a compatible CfSharp state database.</summary>
    InvalidSchema = 3,

    /// <summary>The database was created by a newer unsupported schema version.</summary>
    UnsupportedSchema = 4,

    /// <summary>SQLite reported malformed or corrupt database content.</summary>
    CorruptDatabase = 5,

    /// <summary>Opening, migrating, reading, or writing the database failed.</summary>
    DatabaseFailure = 6,

    /// <summary>The database is already bound to a different sync root.</summary>
    SyncRootMismatch = 7,
}

/// <summary>Represents a failure from the official SQLite durable-state provider.</summary>
/// <remarks>
/// The exception retains SQLite primary and extended result codes when the underlying failure
/// originated in SQLite. A value of zero means no SQLite result code was available. The database
/// path is included for diagnostics but file content, identities, payloads, and credentials are
/// never included in the message or properties.
/// </remarks>
public sealed class SqliteCloudStateStoreException : Exception
{
    internal SqliteCloudStateStoreException(
        SqliteCloudStateStoreError error,
        string databasePath,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        DatabasePath = databasePath;
        if (innerException is SqliteException sqliteException)
        {
            SqliteErrorCode = sqliteException.SqliteErrorCode;
            SqliteExtendedErrorCode = sqliteException.SqliteExtendedErrorCode;
        }
    }

    /// <summary>Gets the stable provider-level failure classification.</summary>
    public SqliteCloudStateStoreError Error { get; }

    /// <summary>Gets the normalized absolute path of the affected state database.</summary>
    public string DatabasePath { get; }

    /// <summary>Gets the SQLite primary result code, or zero for a non-SQLite failure.</summary>
    public int SqliteErrorCode { get; }

    /// <summary>Gets the SQLite extended result code, or zero for a non-SQLite failure.</summary>
    public int SqliteExtendedErrorCode { get; }
}
