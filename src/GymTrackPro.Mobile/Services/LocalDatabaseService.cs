using GymTrackPro.Mobile.Models;
using Microsoft.Extensions.Logging;
using SQLite;
using System.Text.Json;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.Services;

public sealed class LocalDatabaseService : ILocalDatabaseService, IAccountLocalDataCleaner
{
    private readonly object _connectionGate = new();
    private readonly string _databasePath;
    private readonly ILogger<LocalDatabaseService> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private SQLiteAsyncConnection? _connection;
    private volatile bool _initialized;

    private const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);

    public LocalDatabaseService(ILogger<LocalDatabaseService> logger)
        : this(logger, Path.Combine(FileSystem.AppDataDirectory, "GymTrackPro.db3"))
    {
    }

    internal LocalDatabaseService(
        ILogger<LocalDatabaseService> logger,
        string databasePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _databasePath = !string.IsNullOrWhiteSpace(databasePath)
            ? databasePath
            : throw new ArgumentException("A local database path is required.", nameof(databasePath));
    }

    public SQLiteAsyncConnection GetConnection()
    {
        lock (_connectionGate)
        {
            if (_connection is not null)
            {
                return _connection;
            }

            try
            {
                var databaseExists = File.Exists(_databasePath);

                // The database lives in the OS app-private sandbox and this singleton
                // is its sole opener. sqlite-net cannot transfer the synchronous handle
                // validated below into SQLiteAsyncConnection, so same-UID replacement
                // between validation and pool open is an accepted P3 threat assumption.
                SqliteDatabaseGuard.ValidateExistingDatabase(_databasePath);

                var flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache;
                if (!databaseExists)
                {
                    flags |= SQLiteOpenFlags.Create;
                }

                _connection = new SQLiteAsyncConnection(_databasePath, flags);
                return _connection;
            }
            catch (LocalDatabaseCompatibilityException exception)
            {
                _logger.LogCritical(
                    exception,
                    "The local database was preserved because it is unreadable or incompatible with this app version.");
                throw;
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_initialized)
            {
                return;
            }

            var database = GetConnection();
            // Upgrade a legacy queue before sqlite-net attempts to create indexes for
            // properties introduced after the table itself.
            var syncQueueExists = await database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SyncQueue';")
                .ConfigureAwait(false) > 0;
            if (syncQueueExists)
            {
                await EnsureColumnAsync(
                        database,
                        "SyncQueue",
                        "AccountUid",
                        "TEXT NOT NULL DEFAULT ''")
                    .ConfigureAwait(false);
            }

            await database.CreateTableAsync<SyncQueue>().ConfigureAwait(false);
            await database.CreateTableAsync<LocalMember>().ConfigureAwait(false);
            await database.CreateTableAsync<LocalAttendance>().ConfigureAwait(false);
            await database.CreateTableAsync<LocalGoerDashboardCache>().ConfigureAwait(false);

            var schemaVersion = await database.ExecuteScalarAsync<int>("PRAGMA user_version;")
                .ConfigureAwait(false);
            if (schemaVersion < CurrentSchemaVersion)
            {
                await database.ExecuteAsync(
                        "CREATE INDEX IF NOT EXISTS IX_SyncQueue_AccountUid_CreatedAt ON SyncQueue (AccountUid, CreatedAt);")
                    .ConfigureAwait(false);
                await database.ExecuteAsync(
                        "CREATE UNIQUE INDEX IF NOT EXISTS UX_SyncQueue_AccountUid_RecordId ON SyncQueue (AccountUid, RecordId) WHERE AccountUid <> '';")
                    .ConfigureAwait(false);
                await database.ExecuteAsync($"PRAGMA user_version = {CurrentSchemaVersion};")
                    .ConfigureAwait(false);
            }

            _initialized = true;
        }
        catch (Exception exception) when (exception is SQLiteException or IOException or UnauthorizedAccessException)
        {
            var compatibilityException = new LocalDatabaseCompatibilityException(
                "The local database could not be safely initialized. Its files were preserved for recovery.",
                exception);
            _logger.LogCritical(
                compatibilityException,
                "Local database initialization failed without deleting or recreating existing data.");
            throw compatibilityException;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<SQLiteAsyncConnection> GetInitializedConnectionAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        return GetConnection();
    }

    public async Task SaveGoerDashboardAsync(
        string accountUid,
        GoerDashboardDto dashboard,
        IReadOnlyCollection<AttendanceDto> recentAttendance)
    {
        ValidateAccountUid(accountUid);
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentNullException.ThrowIfNull(recentAttendance);

        var database = await GetInitializedConnectionAsync().ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(
            new GoerDashboardCachePayload
            {
                Dashboard = dashboard,
                RecentAttendance = recentAttendance.ToList()
            },
            CacheJsonOptions);
        await database.InsertOrReplaceAsync(new LocalGoerDashboardCache
        {
            AccountUid = accountUid,
            PayloadJson = payload,
            CachedAtUtc = DateTime.UtcNow
        }).ConfigureAwait(false);
    }

    public async Task<GoerDashboardCacheSnapshot?> GetGoerDashboardAsync(string accountUid)
    {
        ValidateAccountUid(accountUid);
        var database = await GetInitializedConnectionAsync().ConfigureAwait(false);
        var cached = await database.Table<LocalGoerDashboardCache>()
            .Where(item => item.AccountUid == accountUid)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        if (cached is null)
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<GoerDashboardCachePayload>(
                cached.PayloadJson,
                CacheJsonOptions);
            return payload?.Dashboard is null
                ? null
                : new GoerDashboardCacheSnapshot(
                    payload.Dashboard,
                    payload.RecentAttendance,
                    cached.CachedAtUtc);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Ignoring an unreadable gym-goer dashboard cache entry.");
            return null;
        }
    }

    public async Task ClearAccountAsync(string accountUid)
    {
        ValidateAccountUid(accountUid);
        var database = await GetInitializedConnectionAsync().ConfigureAwait(false);
        await database.ExecuteAsync(
                "DELETE FROM LocalGoerDashboardCache WHERE AccountUid = ?;",
                accountUid)
            .ConfigureAwait(false);
        await database.ExecuteAsync(
                "DELETE FROM SyncQueue WHERE AccountUid = ? OR AccountUid = '';",
                accountUid)
            .ConfigureAwait(false);
        await database.ExecuteAsync(
                "DELETE FROM LocalAttendance WHERE IsCurrentUser = 1;")
            .ConfigureAwait(false);
    }

    public Task ClearAccountDataAsync(
        string firebaseUid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ClearAccountAsync(firebaseUid);
    }

    private static async Task EnsureColumnAsync(
        SQLiteAsyncConnection database,
        string tableName,
        string columnName,
        string declaration)
    {
        var columns = await database.GetTableInfoAsync(tableName).ConfigureAwait(false);
        if (columns.Any(column => string.Equals(column.Name, columnName, StringComparison.Ordinal)))
        {
            return;
        }

        await database.ExecuteAsync(
                $"ALTER TABLE {tableName} ADD COLUMN {columnName} {declaration};")
            .ConfigureAwait(false);
    }

    private static void ValidateAccountUid(string accountUid)
    {
        if (string.IsNullOrWhiteSpace(accountUid) || accountUid.Length > 128)
        {
            throw new ArgumentException("A valid Firebase account UID is required.", nameof(accountUid));
        }
    }

    private sealed class GoerDashboardCachePayload
    {
        public GoerDashboardDto Dashboard { get; init; } = new();
        public List<AttendanceDto> RecentAttendance { get; init; } = new();
    }
}
