using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1")]
public class TrainerController : ControllerBase
{
    private readonly GymDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;

    public TrainerController(GymDbContext dbContext, ICurrentUserContext currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    [HttpPost("trainers/assign")]
    [Authorize(Policy = Policies.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    public async Task<IActionResult> AssignClient([FromBody] AssignClientDto request)
    {
        var trainer = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.UserID == request.TrainerUserID && u.Role == UserRole.Trainer);
        if (trainer == null)
        {
            return BadRequest(ApiResponse.FailureResponse("The specified user is not a valid Trainer."));
        }

        var member = await _dbContext.Members
            .FirstOrDefaultAsync(m => m.MemberID == request.MemberID);
        if (member == null)
        {
            return BadRequest(ApiResponse.FailureResponse("The specified member does not exist."));
        }

        var existingAssignment = await _dbContext.TrainerClients
            .FirstOrDefaultAsync(tc => tc.TrainerUserID == request.TrainerUserID && tc.MemberID == request.MemberID && tc.IsActive);
        if (existingAssignment != null)
        {
            return Ok(ApiResponse.SuccessResponse("Client is already assigned to this trainer."));
        }

        var assignment = new TrainerClient
        {
            TrainerUserID = request.TrainerUserID,
            MemberID = request.MemberID,
            AssignedAt = DateTime.UtcNow,
            IsActive = true
        };

        _dbContext.TrainerClients.Add(assignment);
        await _dbContext.SaveChangesAsync();

        return Ok(ApiResponse.SuccessResponse("Trainer assigned to client successfully."));
    }

    [HttpPost("trainers/routines")]
    [Authorize(Policy = Policies.TrainerOnly)]
    [ProducesResponseType(typeof(ApiResponse<WorkoutRoutineResponseDto>), 200)]
    public async Task<IActionResult> PostWorkoutRoutine([FromBody] PostRoutineDto request)
    {
        var trainerUserId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("An internally resolved SQL user is required.");

        var assignment = await _dbContext.TrainerClients
            .FirstOrDefaultAsync(tc => tc.TrainerUserID == trainerUserId && tc.MemberID == request.MemberID && tc.IsActive);
        if (assignment == null)
        {
            return BadRequest(ApiResponse.FailureResponse("This member is not assigned to you."));
        }

        if (string.IsNullOrWhiteSpace(request.RoutineName) || request.RoutineName.Length > 100)
        {
            return BadRequest(ApiResponse.FailureResponse("Routine name must be between 1 and 100 characters."));
        }

        try
        {
            var testParse = System.Text.Json.JsonSerializer.Deserialize<List<WorkoutExerciseValidationDto>>(
                request.ExercisesJson, 
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (testParse == null || testParse.Count == 0)
            {
                return BadRequest(ApiResponse.FailureResponse("Exercises list cannot be empty."));
            }
            foreach (var ex in testParse)
            {
                if (string.IsNullOrWhiteSpace(ex.Name) || ex.Name.Length > 100)
                {
                    return BadRequest(ApiResponse.FailureResponse("Each exercise must have a valid name between 1 and 100 characters."));
                }
                if (ex.Sets <= 0 || ex.Reps <= 0)
                {
                    return BadRequest(ApiResponse.FailureResponse("Sets and Reps must be greater than zero."));
                }
                if (ex.Weight < 0 || ex.Weight > 1000)
                {
                    return BadRequest(ApiResponse.FailureResponse("Weight must be between 0 and 1000 kg."));
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest(ApiResponse.FailureResponse("Invalid JSON format for ExercisesJson. It must be a valid JSON array of exercise objects."));
        }

        var routine = await _dbContext.WorkoutRoutines
            .FirstOrDefaultAsync(r => r.MemberID == request.MemberID && r.TrainerUserID == trainerUserId && r.IsActive);

        if (routine == null)
        {
            routine = new WorkoutRoutine
            {
                MemberID = request.MemberID,
                TrainerUserID = trainerUserId,
                RoutineName = request.RoutineName,
                ExercisesJson = request.ExercisesJson,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };
            _dbContext.WorkoutRoutines.Add(routine);
        }
        else
        {
            routine.RoutineName = request.RoutineName;
            routine.ExercisesJson = request.ExercisesJson;
            routine.UpdatedAt = DateTime.UtcNow;
            _dbContext.WorkoutRoutines.Update(routine);
        }

        await _dbContext.SaveChangesAsync();

        var responseDto = new WorkoutRoutineResponseDto
        {
            RoutineID = routine.RoutineID,
            RoutineName = routine.RoutineName,
            ExercisesJson = routine.ExercisesJson,
            UpdatedAt = routine.UpdatedAt
        };

        return Ok(ApiResponse<WorkoutRoutineResponseDto>.SuccessResponse(responseDto, "Workout routine updated successfully."));
    }

    [HttpGet("trainers/clients")]
    [Authorize(Policy = Policies.TrainerOnly)]
    [ProducesResponseType(typeof(ApiResponse<List<AssignedClientDto>>), 200)]
    public async Task<IActionResult> GetAssignedClients()
    {
        var trainerUserId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("An internally resolved SQL user is required.");

        var clients = await _dbContext.TrainerClients
            .Where(tc => tc.TrainerUserID == trainerUserId && tc.IsActive)
            .Select(tc => new AssignedClientDto
            {
                MemberID = tc.Member.MemberID,
                FullName = tc.Member.FirstName + " " + tc.Member.LastName,
                Email = tc.Member.Email ?? string.Empty,
                PhoneNumber = tc.Member.PhoneNumber,
                MembershipStatus = tc.Member.Status
            })
            .ToListAsync();

        return Ok(ApiResponse<List<AssignedClientDto>>.SuccessResponse(clients, "Assigned clients retrieved successfully."));
    }

    [HttpGet("me/workout-routine")]
    [Authorize(Policy = Policies.GymGoerSelf)]
    [ProducesResponseType(typeof(ApiResponse<WorkoutRoutineResponseDto>), 200)]
    public async Task<IActionResult> GetMyWorkoutRoutine()
    {
        var memberId = _currentUser.MemberId
            ?? throw new UnauthorizedAccessException("An internally resolved SQL member is required.");

        var routine = await _dbContext.WorkoutRoutines
            .Where(r => r.MemberID == memberId && r.IsActive)
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => new WorkoutRoutineResponseDto
            {
                RoutineID = r.RoutineID,
                RoutineName = r.RoutineName,
                ExercisesJson = r.ExercisesJson,
                UpdatedAt = r.UpdatedAt
            })
            .FirstOrDefaultAsync();

        if (routine == null)
        {
            return Ok(ApiResponse<WorkoutRoutineResponseDto>.SuccessResponse(new WorkoutRoutineResponseDto
            {
                RoutineName = "No Assigned Workout",
                ExercisesJson = "[]"
            }, "No routine assigned yet."));
        }

        return Ok(ApiResponse<WorkoutRoutineResponseDto>.SuccessResponse(routine, "Workout routine retrieved successfully."));
    }

    [HttpPost("me/workout-logs")]
    [Authorize(Policy = Policies.GymGoerSelf)]
    [ProducesResponseType(typeof(ApiResponse<WorkoutLogResponseDto>), 200)]
    public async Task<IActionResult> LogWorkoutSession([FromBody] PostWorkoutLogDto request)
    {
        var memberId = _currentUser.MemberId
            ?? throw new UnauthorizedAccessException("An internally resolved SQL member is required.");

        if (string.IsNullOrWhiteSpace(request.RoutineName))
        {
            return BadRequest(ApiResponse.FailureResponse("Routine name is required."));
        }

        // Find active trainer for this member if exists
        var activeTrainerId = await _dbContext.TrainerClients
            .Where(tc => tc.MemberID == memberId && tc.IsActive)
            .Select(tc => (int?)tc.TrainerUserID)
            .FirstOrDefaultAsync();

        var log = new WorkoutLog
        {
            MemberID = memberId,
            TrainerUserID = activeTrainerId,
            RoutineName = request.RoutineName,
            CompletedAtUtc = DateTime.UtcNow,
            CompletedExercisesJson = request.CompletedExercisesJson,
            Notes = request.Notes
        };

        _dbContext.WorkoutLogs.Add(log);
        await _dbContext.SaveChangesAsync();

        var response = new WorkoutLogResponseDto
        {
            LogID = log.LogID,
            MemberID = log.MemberID,
            TrainerUserID = log.TrainerUserID,
            RoutineName = log.RoutineName,
            CompletedAtUtc = log.CompletedAtUtc,
            CompletedExercisesJson = log.CompletedExercisesJson,
            Notes = log.Notes
        };

        return Ok(ApiResponse<WorkoutLogResponseDto>.SuccessResponse(response, "Workout session logged successfully!"));
    }

    [HttpGet("me/workout-logs")]
    [Authorize(Policy = Policies.GymGoerSelf)]
    [ProducesResponseType(typeof(ApiResponse<List<WorkoutLogResponseDto>>), 200)]
    public async Task<IActionResult> GetMyWorkoutLogs()
    {
        var memberId = _currentUser.MemberId
            ?? throw new UnauthorizedAccessException("An internally resolved SQL member is required.");

        var logs = await _dbContext.WorkoutLogs
            .Where(l => l.MemberID == memberId)
            .OrderByDescending(l => l.CompletedAtUtc)
            .Select(l => new WorkoutLogResponseDto
            {
                LogID = l.LogID,
                MemberID = l.MemberID,
                TrainerUserID = l.TrainerUserID,
                RoutineName = l.RoutineName,
                CompletedAtUtc = l.CompletedAtUtc,
                CompletedExercisesJson = l.CompletedExercisesJson,
                Notes = l.Notes
            })
            .ToListAsync();

        return Ok(ApiResponse<List<WorkoutLogResponseDto>>.SuccessResponse(logs, "Workout history retrieved successfully."));
    }

    [HttpGet("trainers/clients/{memberId:int}/logs")]
    [Authorize(Policy = Policies.TrainerOnly)]
    [ProducesResponseType(typeof(ApiResponse<List<WorkoutLogResponseDto>>), 200)]
    public async Task<IActionResult> GetClientWorkoutLogs(int memberId)
    {
        var trainerUserId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("An internally resolved SQL user is required.");

        var logs = await _dbContext.WorkoutLogs
            .Where(l => l.MemberID == memberId && l.TrainerUserID == trainerUserId)
            .OrderByDescending(l => l.CompletedAtUtc)
            .Select(l => new WorkoutLogResponseDto
            {
                LogID = l.LogID,
                MemberID = l.MemberID,
                TrainerUserID = l.TrainerUserID,
                RoutineName = l.RoutineName,
                CompletedAtUtc = l.CompletedAtUtc,
                CompletedExercisesJson = l.CompletedExercisesJson,
                Notes = l.Notes
            })
            .ToListAsync();

        return Ok(ApiResponse<List<WorkoutLogResponseDto>>.SuccessResponse(logs, "Client workout logs retrieved successfully."));
    }
}
