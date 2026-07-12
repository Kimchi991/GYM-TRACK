using GymTrackPro.Mobile.Services;
using SQLite;

namespace GymTrackPro.Mobile.Tests;

public sealed class SqliteDatabaseGuardTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "GymTrackPro.Mobile.Tests",
        Guid.NewGuid().ToString("N"));

    static SqliteDatabaseGuardTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public SqliteDatabaseGuardTests()
    {
        Directory.CreateDirectory(_testDirectory);
    }

    [Fact]
    public void Standard_sqlite_database_can_be_opened_written_read_and_validated()
    {
        var databasePath = GetDatabasePath();
        using (var connection = new SQLiteConnection(
                   databasePath,
                   SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex))
        {
            connection.CreateTable<ProbeRow>();
            connection.Insert(new ProbeRow { Value = "round-trip" });
        }

        SqliteDatabaseGuard.ValidateExistingDatabase(databasePath);

        using var reopened = new SQLiteConnection(
            databasePath,
            SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
        Assert.Equal("round-trip", reopened.Table<ProbeRow>().Single().Value);
    }

    [Fact]
    public void Missing_database_is_not_created_by_validation()
    {
        var databasePath = GetDatabasePath();

        SqliteDatabaseGuard.ValidateExistingDatabase(databasePath);

        Assert.False(File.Exists(databasePath));
    }

    [Fact]
    public void Encrypted_or_unknown_header_is_rejected_without_modifying_file()
    {
        var databasePath = GetDatabasePath();
        var originalBytes = Enumerable.Range(1, 64).Select(value => (byte)value).ToArray();
        File.WriteAllBytes(databasePath, originalBytes);

        Assert.Throws<LocalDatabaseCompatibilityException>(() =>
            SqliteDatabaseGuard.ValidateExistingDatabase(databasePath));

        Assert.True(File.Exists(databasePath));
        Assert.Equal(originalBytes, File.ReadAllBytes(databasePath));
    }

    [Fact]
    public void Corrupt_standard_header_is_rejected_without_modifying_file()
    {
        var databasePath = GetDatabasePath();
        var originalBytes = new byte[128];
        "SQLite format 3\0"u8.CopyTo(originalBytes);
        File.WriteAllBytes(databasePath, originalBytes);

        Assert.Throws<LocalDatabaseCompatibilityException>(() =>
            SqliteDatabaseGuard.ValidateExistingDatabase(databasePath));

        Assert.Equal(originalBytes, File.ReadAllBytes(databasePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private string GetDatabasePath() => Path.Combine(_testDirectory, "probe.db3");

    [Table("Probe")]
    private sealed class ProbeRow
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }
}
