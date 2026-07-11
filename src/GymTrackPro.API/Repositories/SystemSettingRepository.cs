using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Repositories;

public class SystemSettingRepository : ISystemSettingRepository
{
    private readonly GymDbContext _context;

    public SystemSettingRepository(GymDbContext context)
    {
        _context = context;
    }

    public async Task<GymSetting?> GetByKeyAsync(string key)
    {
        return await _context.GymSettings
            .FirstOrDefaultAsync(s => s.SettingKey == key);
    }

    public async Task<IEnumerable<GymSetting>> GetAllAsync()
    {
        return await _context.GymSettings
            .ToListAsync();
    }

    public async Task UpdateAsync(GymSetting setting)
    {
        _context.GymSettings.Update(setting);
        await _context.SaveChangesAsync();
    }
}
