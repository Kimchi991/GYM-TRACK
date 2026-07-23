using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("TrainerClients")]
public class TrainerClient
{
    [Key]
    public int TrainerClientID { get; set; }

    [Required]
    public int TrainerUserID { get; set; }
    [ForeignKey("TrainerUserID")]
    public User TrainerUser { get; set; } = null!;

    [Required]
    public int MemberID { get; set; }
    [ForeignKey("MemberID")]
    public Member Member { get; set; } = null!;

    [Required]
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public bool IsActive { get; set; } = true;
}
