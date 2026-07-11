using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Shared.Entities;

[Table("GymInvitations")]
public class GymInvitation
{
    [Key]
    public int InvitationID { get; set; }

    [Required]
    public int GymID { get; set; }

    [ForeignKey("GymID")]
    public Gym? Gym { get; set; }

    [Required]
    public UserRole Role { get; set; }

    [Required]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Token { get; set; } = string.Empty;

    [Required]
    public DateTime ExpiresAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    [Required]
    public int CreatedBy { get; set; }
}
