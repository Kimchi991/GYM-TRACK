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

    public async Task<SystemSetting?> GetByKeyAsync(string key)
    {
        return await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.SettingKey == key);
    }

    public async Task<IEnumerable<SystemSetting>> GetAllAsync()
    {
        return await _context.SystemSettings
            .ToListAsync();
    }

    public async Task UpdateAsync(SystemSetting setting)
    {
        _context.SystemSettings.Update(setting);
        await _context.SaveChangesAsync();
    }
}
