using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("Members")]
public class Member
{
    [Key]
    public int MemberID { get; set; }

    [Required]
    public int GymID { get; set; }

    [ForeignKey("GymID")]
    public Gym? Gym { get; set; }

    public int? UserID { get; set; }

    [ForeignKey("UserID")]
    public User? User { get; set; }

    [Required]
    [StringLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string Gender { get; set; } = string.Empty;

    [Required]
    public DateTime BirthDate { get; set; }

    [Required]
    [StringLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Email { get; set; }

    [StringLength(255)]
    public string? Address { get; set; }

    [Required]
    [StringLength(100)]
    public string EmergencyContact { get; set; } = string.Empty;

    public string? ProfilePicture { get; set; }

    [Required]
    [StringLength(100)]
    public string QRCode { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = "Active";

    [Required]
    public DateTime DateRegistered { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    [Required]
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public int? DeletedBy { get; set; }
}
