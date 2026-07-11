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

    public override async Task<User?> GetByIdAsync(int id)
    {
        return await _dbSet.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.UserID == id);
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await _dbSet.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _dbSet.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<bool> UsernameExistsAsync(string username)
    {
        return await _dbSet.IgnoreQueryFilters().AnyAsync(u => u.Username == username);
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _dbSet.IgnoreQueryFilters().AnyAsync(u => u.Email == email);
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
}
