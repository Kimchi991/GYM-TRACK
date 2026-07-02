using System.Threading.Tasks;
using SQLite;

namespace GymTrackPro.Mobile.Services;

public interface ILocalDatabaseService
{
    SQLiteAsyncConnection GetConnection();
    Task InitializeAsync();
}
