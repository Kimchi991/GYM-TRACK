using SQLite;

namespace GymTrackPro.Mobile.Services;

internal static class SqliteDatabaseGuard
{
    private static ReadOnlySpan<byte> SqliteHeader => "SQLite format 3\0"u8;

    internal static void ValidateExistingDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("A database path is required.", nameof(databasePath));
        }

        if (!File.Exists(databasePath))
        {
            return;
        }

        try
        {
            ValidateHeader(databasePath);

            using var connection = new SQLiteConnection(
                databasePath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
            var result = connection.ExecuteScalar<string>("PRAGMA quick_check(1)");
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new LocalDatabaseCompatibilityException(
                    "The existing local database failed SQLite integrity validation.");
            }
        }
        catch (LocalDatabaseCompatibilityException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SQLiteException or IOException or UnauthorizedAccessException)
        {
            throw new LocalDatabaseCompatibilityException(
                "The existing local database is unreadable or incompatible. It was not deleted or recreated.",
                exception);
        }
    }

    private static void ValidateHeader(string databasePath)
    {
        Span<byte> actualHeader = stackalloc byte[16];
        using var stream = new FileStream(
            databasePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        if (stream.Read(actualHeader) != actualHeader.Length ||
            !actualHeader.SequenceEqual(SqliteHeader))
        {
            // SQLCipher databases created by the abandoned implementation have an
            // encrypted header. Preserve both the DB and its SecureStorage key so a
            // deliberate migration can recover them later.
            throw new LocalDatabaseCompatibilityException(
                "The existing local database is not a standard SQLite file. It may require SQLCipher migration.");
        }
    }
}
