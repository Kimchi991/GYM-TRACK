using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GymTrackPro.Shared.Entities;

[Table("WorkoutRoutines")]
public class WorkoutRoutine
{
    [Key]
    public int RoutineID { get; set; }

    [Required]
    public int MemberID { get; set; }
    [ForeignKey("MemberID")]
    public Member Member { get; set; } = null!;

    [Required]
    public int TrainerUserID { get; set; }
    [ForeignKey("TrainerUserID")]
    public User TrainerUser { get; set; } = null!;

    [Required]
    [StringLength(100)]
    public string RoutineName { get; set; } = string.Empty;

    [Required]
    public string ExercisesJson { get; set; } = "[]";

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public bool IsActive { get; set; } = true;
}
