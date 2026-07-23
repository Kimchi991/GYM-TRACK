using System;
using System.Collections.Generic;

namespace GymTrackPro.Shared.DTOs;

public class AssignClientDto
{
    public int TrainerUserID { get; set; }
    public int MemberID { get; set; }
}

public class PostRoutineDto
{
    public int MemberID { get; set; }
    public string RoutineName { get; set; } = string.Empty;
    public string ExercisesJson { get; set; } = "[]";
}

public class AssignedClientDto
{
    public int MemberID { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string MembershipStatus { get; set; } = string.Empty;
}

public class WorkoutRoutineResponseDto
{
    public int RoutineID { get; set; }
    public string RoutineName { get; set; } = string.Empty;
    public string ExercisesJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; }
}

public class WorkoutExerciseValidationDto
{
    public string Name { get; set; } = string.Empty;
    public int Sets { get; set; }
    public int Reps { get; set; }
    public decimal Weight { get; set; }
}

public class PostWorkoutLogDto
{
    public string RoutineName { get; set; } = string.Empty;
    public string CompletedExercisesJson { get; set; } = "[]";
    public string? Notes { get; set; }
}

public class WorkoutLogResponseDto
{
    public int LogID { get; set; }
    public int MemberID { get; set; }
    public int? TrainerUserID { get; set; }
    public string RoutineName { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
    public string CompletedExercisesJson { get; set; } = "[]";
    public string? Notes { get; set; }
}
