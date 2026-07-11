using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Shared.Interfaces;

public interface ISystemSettingRepository
{
    Task<GymSetting?> GetByKeyAsync(string key);
    Task<IEnumerable<GymSetting>> GetAllAsync();
    Task UpdateAsync(GymSetting setting);
}
