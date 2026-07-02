using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Shared.Interfaces;

public interface ISystemSettingRepository
{
    Task<SystemSetting?> GetByKeyAsync(string key);
    Task<IEnumerable<SystemSetting>> GetAllAsync();
    Task UpdateAsync(SystemSetting setting);
}
