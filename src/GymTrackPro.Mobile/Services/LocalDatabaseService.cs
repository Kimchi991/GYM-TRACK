using System;
using System.IO;
using System.Threading.Tasks;
using SQLite;
using GymTrackPro.Mobile.Models;

namespace GymTrackPro.Mobile.Services;

public class LocalDatabaseService : ILocalDatabaseService
{
    private SQLiteAsyncConnection? _connection;
    private readonly string _databasePath;

    public LocalDatabaseService()
    {
        _databasePath = Path.Combine(FileSystem.AppDataDirectory, "GymTrackPro.db3");
    }

    public SQLiteAsyncConnection GetConnection()
    {
        if (_connection == null)
        {
            _connection = new SQLiteAsyncConnection(_databasePath, 
                SQLiteOpenFlags.ReadWrite | 
                SQLiteOpenFlags.Create | 
                SQLiteOpenFlags.SharedCache);
        }
        return _connection;
    }

    public async Task InitializeAsync()
    {
        var db = GetConnection();
        
        // Create local tables
        await db.CreateTableAsync<SyncQueue>();
        await db.CreateTableAsync<LocalMember>();
        
        // TODO: Scaffold other local caching tables (LocalSubscription, LocalAttendance, etc.) in Phase 5
    }
}
