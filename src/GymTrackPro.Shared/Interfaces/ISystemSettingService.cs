using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface ISystemSettingService
{
    Task<string> GetValueAsync(string key, string defaultValue = "");
    Task<int> GetValueIntAsync(string key, int defaultValue = 0);
    Task<long> GetValueLongAsync(string key, long defaultValue = 0L);
    Task<double> GetValueDoubleAsync(string key, double defaultValue = 0.0);
    Task<bool> GetValueBoolAsync(string key, bool defaultValue = false);
    Task<IEnumerable<SystemSettingDto>> GetAllSettingsAsync();
    Task UpdateSettingAsync(string key, string value);
}
