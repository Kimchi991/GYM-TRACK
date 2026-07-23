using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("WorkoutLogs")]
public class WorkoutLog
{
    [Key]
    public int LogID { get; set; }

    [Required]
    public int MemberID { get; set; }

    public int? TrainerUserID { get; set; }

    [Required]
    [StringLength(100)]
    public string RoutineName { get; set; } = string.Empty;

    [Required]
    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;

    [Required]
    public string CompletedExercisesJson { get; set; } = "[]";

    [StringLength(255)]
    public string? Notes { get; set; }

    [ForeignKey(nameof(MemberID))]
    public virtual Member Member { get; set; } = null!;

    [ForeignKey(nameof(TrainerUserID))]
    public virtual User? TrainerUser { get; set; }
}
