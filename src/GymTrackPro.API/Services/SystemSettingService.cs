using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class SystemSettingService : ISystemSettingService
{
    private readonly ISystemSettingRepository _repository;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SystemSettingService(
        ISystemSettingRepository repository,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor)
    {
        _repository = repository;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
    }

    private int? GetCurrentUserId()
    {
        var nameIdentifier = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(nameIdentifier, out int userId) ? userId : null;
    }

    private string GetClientIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    public async Task<string> GetValueAsync(string key, string defaultValue = "")
    {
        var setting = await _repository.GetByKeyAsync(key);
        return setting != null ? setting.SettingValue : defaultValue;
    }

    public async Task<int> GetValueIntAsync(string key, int defaultValue = 0)
    {
        var str = await GetValueAsync(key, null!);
        return str != null && int.TryParse(str, out var result) ? result : defaultValue;
    }

    public async Task<long> GetValueLongAsync(string key, long defaultValue = 0L)
    {
        var str = await GetValueAsync(key, null!);
        return str != null && long.TryParse(str, out var result) ? result : defaultValue;
    }

    public async Task<double> GetValueDoubleAsync(string key, double defaultValue = 0.0)
    {
        var str = await GetValueAsync(key, null!);
        return str != null && double.TryParse(str, out var result) ? result : defaultValue;
    }

    public async Task<bool> GetValueBoolAsync(string key, bool defaultValue = false)
    {
        var str = await GetValueAsync(key, null!);
        return str != null && bool.TryParse(str, out var result) ? result : defaultValue;
    }

    public async Task<IEnumerable<SystemSettingDto>> GetAllSettingsAsync()
    {
        var settings = await _repository.GetAllAsync();
        return settings.Select(s => new SystemSettingDto
        {
            SettingKey = s.SettingKey,
            SettingValue = s.SettingValue,
            GroupName = s.GroupName,
            Description = s.Description,
            LastModified = s.LastModified
        }).ToList();
    }

    public async Task UpdateSettingAsync(string key, string value)
    {
        var setting = await _repository.GetByKeyAsync(key);
        if (setting == null)
        {
            throw new KeyNotFoundException($"System setting with key '{key}' was not found.");
        }

        var oldValue = setting.SettingValue;
        setting.SettingValue = value;
        setting.LastModified = DateTime.UtcNow;

        await _repository.UpdateAsync(setting);

        // Security Audit logging for configuration changes
        await _auditService.LogActivityAsync(
            GetCurrentUserId(),
            "System Setting Modified",
            $"Configuration key '{key}' changed from '{oldValue}' to '{value}'.",
            GetClientIpAddress()
        );
    }
}
