using System.Threading.Tasks;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Shared.Interfaces;

public interface IUserRepository : IBaseRepository<User>
{
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByEmailAsync(string email);
    Task<bool> UsernameExistsAsync(string username);
    Task<bool> EmailExistsAsync(string email);
    Task<User?> GetByFirebaseUidAsync(string firebaseUid);
    Task UpdateLastLoginAsync(int userId);
}
