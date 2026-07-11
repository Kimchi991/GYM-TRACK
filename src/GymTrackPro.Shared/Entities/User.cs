using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Entities;

[Table("Users")]
public class User
{
    [Key]
    public int UserID { get; set; }

    public int? GymID { get; set; }

    [Required]
    [StringLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    public UserRole Role { get; set; }

    [Required]
    public bool IsActive { get; set; } = true;

    [Required]
    public bool EmailVerified { get; set; } = false;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    [StringLength(100)]
    public string? VerificationToken { get; set; }

    [StringLength(100)]
    public string? ResetToken { get; set; }

    public DateTime? ResetTokenExpires { get; set; }
}
