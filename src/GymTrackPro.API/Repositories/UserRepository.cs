using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.API.Repositories;

public class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(GymDbContext context) : base(context)
    {
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await _dbSet.FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _dbSet.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<bool> UsernameExistsAsync(string username)
    {
        return await _dbSet.AnyAsync(u => u.Username == username);
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _dbSet.AnyAsync(u => u.Email == email);
    }

    public async Task UpdateLastLoginAsync(int userId)
    {
        var user = await GetByIdAsync(userId);
        if (user != null)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await UpdateAsync(user);
        }
    }

    public async Task<User?> GetByFirebaseUidAsync(string firebaseUid)
    {
        return await _dbSet.FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);
    }
}
