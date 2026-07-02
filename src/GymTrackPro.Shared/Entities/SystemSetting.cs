using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("SystemSettings")]
public class SystemSetting
{
    [Key]
    [Required]
    [StringLength(100)]
    public string SettingKey { get; set; } = string.Empty;

    [Required]
    public string SettingValue { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string GroupName { get; set; } = "General";

    [StringLength(255)]
    public string? Description { get; set; }

    [Required]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
