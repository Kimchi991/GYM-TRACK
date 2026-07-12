using GymTrackPro.Mobile.Models;
using Microsoft.Extensions.Logging;
using SQLite;

namespace GymTrackPro.Mobile.Services;

public sealed class LocalDatabaseService : ILocalDatabaseService
{
    private readonly object _connectionGate = new();
    private readonly string _databasePath;
    private readonly ILogger<LocalDatabaseService> _logger;
    private SQLiteAsyncConnection? _connection;

    public LocalDatabaseService(ILogger<LocalDatabaseService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _databasePath = Path.Combine(FileSystem.AppDataDirectory, "GymTrackPro.db3");
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
        var database = GetConnection();

        try
        {
            // These are the pre-existing operational tables. Gym Goer cache tables and
            // per-account AES-GCM envelopes are intentionally deferred to WP5.
            await database.CreateTableAsync<SyncQueue>().ConfigureAwait(false);
            await database.CreateTableAsync<LocalMember>().ConfigureAwait(false);
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
    }
}
