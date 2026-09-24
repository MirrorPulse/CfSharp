using CfSharp.Tests.Persistence;
using Microsoft.Data.Sqlite;

namespace CfSharp.Storage.Sqlite.Tests;

public sealed class SqliteCloudStateStoreTests : CloudStateStoreContractTests, IAsyncLifetime
{
    private string _temporaryDirectory = null!;
    private string _syncRootPath = null!;
    private string _databasePath = null!;
    private string? _directoryLinkPath;
    private string? _sidecarLinkPath;

    public Task InitializeAsync()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "CfSharp-sqlite-tests",
            Guid.NewGuid().ToString("N"));
        _syncRootPath = Path.Combine(_temporaryDirectory, "sync-root");
        _databasePath = Path.Combine(_temporaryDirectory, "state", "cfsharp.db");
        Directory.CreateDirectory(_syncRootPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_directoryLinkPath is not null && Directory.Exists(_directoryLinkPath))
        {
            Directory.Delete(_directoryLinkPath);
        }

        if (_sidecarLinkPath is not null && File.Exists(_sidecarLinkPath))
        {
            File.Delete(_sidecarLinkPath);
        }

        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    protected override ICloudStateStoreFactory CreateFactory() =>
        new SqliteCloudStateStoreFactory(_databasePath);

    protected override CloudStateStoreContext CreateContext() => new(_syncRootPath);

    [Fact]
    public async Task CommitDoesNotReportCleanupCallbackFailureAsCommitFailure()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using SqliteTransaction nativeTransaction =
            (SqliteTransaction)await connection.BeginTransactionAsync();
        int completionCalls = 0;
        await using SqliteCloudStateTransaction transaction = new(
            connection,
            nativeTransaction,
            "in-memory",
            () =>
            {
                completionCalls++;
                throw new InvalidOperationException("test cleanup failure");
            });

        await transaction.CommitAsync();

        Assert.Equal(1, completionCalls);
        await transaction.DisposeAsync();
    }

    [Fact]
    public async Task CheckpointPrefixQueryListsOnlyTheRequestedDirectorySubtree()
    {
        await using ICloudStateStore store = await CreateFactory().OpenAsync(CreateContext());
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using (ICloudStateTransaction write = await store.BeginTransactionAsync())
        {
            await write.Checkpoints.UpsertAsync(
                new CloudStateCheckpoint("cfsharp.directory.\\root", [1], now));
            await write.Checkpoints.UpsertAsync(
                new CloudStateCheckpoint("cfsharp.directory.\\root\\child", [2], now));
            await write.Checkpoints.UpsertAsync(
                new CloudStateCheckpoint("cfsharp.directory.\\other", [3], now));
            await write.CommitAsync();
        }

        await using ICloudStateTransaction read = await store.BeginTransactionAsync();
        IReadOnlyList<CloudStateCheckpoint> checkpoints = await read.Checkpoints
            .ListAsync("cfsharp.directory.\\root");
        Assert.Equal(
            ["cfsharp.directory.\\root", "cfsharp.directory.\\root\\child"],
            checkpoints.Select(checkpoint => checkpoint.Name).ToArray());
        await read.RollbackAsync();
    }

    [Fact]
    public async Task DatabaseInsideSyncRootIsRejected()
    {
        SqliteCloudStateStoreFactory factory = new(
            Path.Combine(_syncRootPath, "state", "cfsharp.db"));

        SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
            SqliteCloudStateStoreException>(async () =>
                await factory.OpenAsync(CreateContext()));

        Assert.Equal(SqliteCloudStateStoreError.PathInsideSyncRoot, exception.Error);
    }

    [Fact]
    public async Task DirectoryLinkIntoSyncRootIsRejected()
    {
        string target = Path.Combine(_syncRootPath, "state");
        Directory.CreateDirectory(target);
        _directoryLinkPath = Path.Combine(_temporaryDirectory, "linked-state");
        Directory.CreateSymbolicLink(_directoryLinkPath, target);
        SqliteCloudStateStoreFactory factory = new(
            Path.Combine(_directoryLinkPath, "cfsharp.db"));

        SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
            SqliteCloudStateStoreException>(async () =>
                await factory.OpenAsync(CreateContext()));

        Assert.Equal(SqliteCloudStateStoreError.PathInsideSyncRoot, exception.Error);
    }

    [Fact]
    public async Task ReparsePointWalSidecarIsRejected()
    {
        await using (ICloudStateStore store = await CreateFactory().OpenAsync(CreateContext()))
        {
        }

        string target = Path.Combine(_temporaryDirectory, "wal-target");
        await File.WriteAllTextAsync(target, "sidecar target");
        _sidecarLinkPath = _databasePath + "-wal";
        File.CreateSymbolicLink(_sidecarLinkPath, target);

        SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
            SqliteCloudStateStoreException>(async () =>
                await CreateFactory().OpenAsync(CreateContext()));

        Assert.Equal(SqliteCloudStateStoreError.InvalidPath, exception.Error);
    }

    [Fact]
    public async Task OwnershipIsExclusiveAndReleasedByDisposal()
    {
        SqliteCloudStateStoreFactory factory = new(_databasePath);
        ICloudStateStore first = await factory.OpenAsync(CreateContext());
        try
        {
            SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
                SqliteCloudStateStoreException>(async () =>
                    await factory.OpenAsync(CreateContext()));
            Assert.Equal(SqliteCloudStateStoreError.AlreadyInUse, exception.Error);
        }
        finally
        {
            await first.DisposeAsync();
        }

        await using ICloudStateStore reopened = await factory.OpenAsync(CreateContext());
    }

    [Fact]
    public async Task DatabaseCannotBeReusedForAnotherSyncRoot()
    {
        await using (ICloudStateStore store = await CreateFactory().OpenAsync(CreateContext()))
        {
        }

        string otherRoot = Path.Combine(_temporaryDirectory, "other-sync-root");
        Directory.CreateDirectory(otherRoot);
        SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
            SqliteCloudStateStoreException>(async () =>
                await CreateFactory().OpenAsync(new CloudStateStoreContext(otherRoot)));

        Assert.Equal(SqliteCloudStateStoreError.SyncRootMismatch, exception.Error);
    }

    [Fact]
    public async Task StoreDisposalRejectsAnActiveTransactionWithoutReleasingOwnership()
    {
        SqliteCloudStateStoreFactory factory = new(_databasePath);
        ICloudStateStore store = await factory.OpenAsync(CreateContext());
        ICloudStateTransaction transaction = await store.BeginTransactionAsync();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await store.DisposeAsync());

            SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
                SqliteCloudStateStoreException>(async () =>
                    await factory.OpenAsync(CreateContext()));
            Assert.Equal(SqliteCloudStateStoreError.AlreadyInUse, exception.Error);
        }
        finally
        {
            await transaction.DisposeAsync();
            await store.DisposeAsync();
        }
    }

    [Fact]
    public async Task VersionZeroSchemaMigratesToCurrentVersion()
    {
        await ExecuteSqlAsync(
            _databasePath,
            """
            CREATE TABLE cfsharp_schema (
                singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
                version INTEGER NOT NULL
            );
            INSERT INTO cfsharp_schema(singleton, version) VALUES(1, 0);
            """);

        SqliteCloudStateStoreFactory factory = new(_databasePath);
        await using (ICloudStateStore store = await factory.OpenAsync(CreateContext()))
        {
        }

        Assert.Equal(
            3L,
            await ExecuteScalarInt64Async(
                _databasePath,
                "SELECT version FROM cfsharp_schema WHERE singleton = 1;"));
        Assert.Equal(
            1L,
            await ExecuteScalarInt64Async(
                _databasePath,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'items';"));
    }

    [Fact]
    public async Task VersionOneRemoteBatchSchemaMigratesToVersionThree()
    {
        await using (ICloudStateStore store = await CreateFactory().OpenAsync(CreateContext()))
        {
        }

        await ExecuteSqlAsync(
            _databasePath,
            "UPDATE cfsharp_schema SET version = 1 WHERE singleton = 1;");

        await using (ICloudStateStore store = await CreateFactory().OpenAsync(CreateContext()))
        {
        }

        Assert.Equal(
            3L,
            await ExecuteScalarInt64Async(
                _databasePath,
                "SELECT version FROM cfsharp_schema WHERE singleton = 1;"));
        Assert.Equal(
            2L,
            await ExecuteScalarInt64Async(
                _databasePath,
                "SELECT COUNT(*) FROM pragma_table_info('remote_batches') " +
                "WHERE name IN ('fingerprint', 'last_change_id');"));
    }

    [Fact]
    public async Task VersionTwoEchoSuppressionSchemaMigratesToVersionThree()
    {
        await using (ICloudStateStore store = await CreateFactory().OpenAsync(CreateContext()))
        {
        }

        await ExecuteSqlAsync(
            _databasePath,
            "UPDATE cfsharp_schema SET version = 2 WHERE singleton = 1;");

        await using (ICloudStateStore store = await CreateFactory().OpenAsync(CreateContext()))
        {
        }

        Assert.Equal(
            3L,
            await ExecuteScalarInt64Async(
                _databasePath,
                "SELECT version FROM cfsharp_schema WHERE singleton = 1;"));
        Assert.Equal(
            2L,
            await ExecuteScalarInt64Async(
                _databasePath,
                "SELECT COUNT(*) FROM pragma_table_info('echo_suppressions') " +
                "WHERE name IN ('previous_relative_path', 'remaining_observations');"));
    }

    [Fact]
    public async Task NewerSchemaVersionIsRejected()
    {
        await ExecuteSqlAsync(
            _databasePath,
            """
            CREATE TABLE cfsharp_schema (
                singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
                version INTEGER NOT NULL
            );
            INSERT INTO cfsharp_schema(singleton, version) VALUES(1, 4);
            """);

        SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
            SqliteCloudStateStoreException>(async () =>
                await CreateFactory().OpenAsync(CreateContext()));

        Assert.Equal(SqliteCloudStateStoreError.UnsupportedSchema, exception.Error);
    }

    [Fact]
    public async Task UnrecognizedSqliteDatabaseIsRejected()
    {
        await ExecuteSqlAsync(_databasePath, "CREATE TABLE application_data(value TEXT NOT NULL);");

        SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
            SqliteCloudStateStoreException>(async () =>
                await CreateFactory().OpenAsync(CreateContext()));

        Assert.Equal(SqliteCloudStateStoreError.InvalidSchema, exception.Error);
    }

    [Fact]
    public async Task VersionOneDatabaseMissingRequiredTableIsRejected()
    {
        await ExecuteSqlAsync(
            _databasePath,
            """
            CREATE TABLE cfsharp_schema (
                singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
                version INTEGER NOT NULL
            );
            INSERT INTO cfsharp_schema(singleton, version) VALUES(1, 1);
            """);

        SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
            SqliteCloudStateStoreException>(async () =>
                await CreateFactory().OpenAsync(CreateContext()));

        Assert.Equal(SqliteCloudStateStoreError.InvalidSchema, exception.Error);
    }

    [Fact]
    public async Task InitializationEnablesWalAndConnectionSafetyPragmas()
    {
        await using (ICloudStateStore store = await CreateFactory().OpenAsync(CreateContext()))
        {
        }

        Assert.Equal("wal", await ExecuteScalarStringAsync(_databasePath, "PRAGMA journal_mode;"));

        await using SqliteConnection connection = new(CreateConnectionString(_databasePath));
        await connection.OpenAsync();
        await SqliteSchema.ConfigureConnectionAsync(connection, 1234, CancellationToken.None);
        Assert.Equal(1L, await ExecuteScalarInt64Async(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(1234L, await ExecuteScalarInt64Async(connection, "PRAGMA busy_timeout;"));
    }

    [Fact]
    public async Task StoreConnectionsEnforceForeignKeysAndPreserveSqliteCodes()
    {
        await using ICloudStateStore store = await CreateFactory().OpenAsync(CreateContext());
        await using ICloudStateTransaction transaction = await store.BeginTransactionAsync();

        SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
            SqliteCloudStateStoreException>(async () =>
                await transaction.Operations.EnqueueAsync(
                    new CloudOperationJournalEntry(
                        Guid.NewGuid(),
                        CloudStateOperationKind.Create,
                        Guid.NewGuid(),
                        [],
                        DateTimeOffset.UtcNow)));

        Assert.Equal(SqliteCloudStateStoreError.DatabaseFailure, exception.Error);
        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.NotEqual(0, exception.SqliteExtendedErrorCode);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task InvalidPersistedValueIsReportedAsInvalidSchema()
    {
        await using (ICloudStateStore store = await CreateFactory().OpenAsync(CreateContext()))
        {
        }

        await ExecuteSqlAsync(
            _databasePath,
            """
            INSERT INTO items(
                item_id, remote_id, relative_path, kind, remote_revision,
                local_file_id, is_tombstone, updated_at_ticks)
            VALUES('not-a-guid', 'remote-invalid', 'invalid.txt', 0, NULL, NULL, 0, 0);
            """);

        await using ICloudStateStore reopened = await CreateFactory().OpenAsync(CreateContext());
        await using ICloudStateTransaction transaction = await reopened.BeginTransactionAsync();
        SqliteCloudStateStoreException exception = await Assert.ThrowsAsync<
            SqliteCloudStateStoreException>(async () =>
                await transaction.Items.GetByRemoteIdAsync("remote-invalid"));

        Assert.Equal(SqliteCloudStateStoreError.InvalidSchema, exception.Error);
        await transaction.RollbackAsync();
    }

    private static async Task ExecuteSqlAsync(string databasePath, string sql)
    {
        string? parent = Path.GetDirectoryName(databasePath);
        Assert.False(string.IsNullOrEmpty(parent));
        Directory.CreateDirectory(parent);
        await using SqliteConnection connection = new(CreateConnectionString(databasePath));
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ExecuteScalarInt64Async(string databasePath, string sql)
    {
        await using SqliteConnection connection = new(CreateConnectionString(databasePath));
        await connection.OpenAsync();
        return await ExecuteScalarInt64Async(connection, sql);
    }

    private static async Task<long> ExecuteScalarInt64Async(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ExecuteScalarStringAsync(string databasePath, string sql)
    {
        await using SqliteConnection connection = new(CreateConnectionString(databasePath));
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
}
