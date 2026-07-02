using System;
using System.ComponentModel.DataAnnotations;

namespace GymTrackPro.Shared.DTOs;

public class SystemSettingDto
{
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string GroupName { get; set; } = "General";
    public string? Description { get; set; }
    public DateTime LastModified { get; set; }
}

public class UpdateSettingDto
{
    [Required]
    public string SettingValue { get; set; } = string.Empty;
}
