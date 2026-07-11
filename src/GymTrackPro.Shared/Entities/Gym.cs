using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("Gyms")]
public class Gym
{
    [Key]
    public int GymID { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(255)]
    public string? Address { get; set; }

    [StringLength(50)]
    public string? ContactNumber { get; set; }

    [StringLength(255)]
    public string? LogoUrl { get; set; }

    [StringLength(255)]
    public string? CoverPhotoUrl { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(255)]
    public string? OperatingHours { get; set; }

    [StringLength(100)]
    public string? Email { get; set; }

    [StringLength(100)]
    public string? Website { get; set; }

    [StringLength(100)]
    public string? Facebook { get; set; }

    [StringLength(100)]
    public string? Instagram { get; set; }

    [StringLength(500)]
    public string? Amenities { get; set; }

    public int Capacity { get; set; }

    [StringLength(100)]
    public string? BusinessPermitNumber { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public int? DeletedBy { get; set; }
}
