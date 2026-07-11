using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Exposes endpoints for tenant registration, plan selection, settings seeding, and initial user creation.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class OnboardingController : ControllerBase
{
    private readonly GymDbContext _dbContext;

    public OnboardingController(GymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Registers a new gym tenant, assigns the selected subscription plan, copies default settings, and creates the owner user account.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<OnboardingResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    public async Task<IActionResult> Register([FromBody] TenantOnboardingDto dto)
    {
        if (dto == null)
        {
            return BadRequest(ApiResponse.FailureResponse("Request body cannot be null."));
        }

        if (string.IsNullOrWhiteSpace(dto.GymName))
        {
            return BadRequest(ApiResponse.FailureResponse("Gym Name is required."));
        }

        if (string.IsNullOrWhiteSpace(dto.AdminEmail) || string.IsNullOrWhiteSpace(dto.AdminUsername))
        {
            return BadRequest(ApiResponse.FailureResponse("Admin Email and Username are required."));
        }

        // Validate plan
        var plan = await _dbContext.SubscriptionPlans.FindAsync(dto.PlanID);
        if (plan == null)
        {
            return BadRequest(ApiResponse.FailureResponse($"Subscription plan with ID {dto.PlanID} does not exist."));
        }

        // Check if admin user already exists (emails and usernames must be globally unique)
        var usernameExists = await _dbContext.Users.AnyAsync(u => u.Username == dto.AdminUsername);
        if (usernameExists)
        {
            return BadRequest(ApiResponse.FailureResponse("Username is already taken."));
        }

        var emailExists = await _dbContext.Users.AnyAsync(u => u.Email == dto.AdminEmail);
        if (emailExists)
        {
            return BadRequest(ApiResponse.FailureResponse("Email is already registered."));
        }

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            // 1. Create Gym Tenant
            var gym = new Gym
            {
                Name = dto.GymName,
                Address = dto.GymAddress,
                ContactNumber = dto.GymContactNumber,
                Capacity = plan.MaxMembers,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _dbContext.Gyms.Add(gym);
            await _dbContext.SaveChangesAsync(); // Generates GymID

            // 2. Create Gym Subscription Link
            var subscription = new GymSubscription
            {
                GymID = gym.GymID,
                PlanID = plan.PlanID,
                Status = SubscriptionStatus.Active,
                StartedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMonths(plan.BillingCycleMonths),
                TrialEndsAt = DateTime.UtcNow.AddDays(14)
            };

            _dbContext.GymSubscriptions.Add(subscription);

            // 3. Seed Default GymSettings for this tenant
            var defaultSettings = new[]
            {
                new GymSetting { GymID = gym.GymID, SettingKey = "GymName", SettingValue = gym.Name, GroupName = "General", Description = "Name of the gym facility.", LastModified = DateTime.UtcNow },
                new GymSetting { GymID = gym.GymID, SettingKey = "Currency", SettingValue = "PHP", GroupName = "General", Description = "Currency code used for financial billing transactions.", LastModified = DateTime.UtcNow },
                new GymSetting { GymID = gym.GymID, SettingKey = "Timezone", SettingValue = "Asia/Manila", GroupName = "General", Description = "System local timezone identifier.", LastModified = DateTime.UtcNow },
                new GymSetting { GymID = gym.GymID, SettingKey = "ContactNumber", SettingValue = gym.ContactNumber ?? "+639170000000", GroupName = "General", Description = "Gym contact helpline phone number.", LastModified = DateTime.UtcNow },
                new GymSetting { GymID = gym.GymID, SettingKey = "QRPrefix", SettingValue = "GTP-", GroupName = "Membership", Description = "Format prefix added to automatically generated member QR codes.", LastModified = DateTime.UtcNow },
                new GymSetting { GymID = gym.GymID, SettingKey = "ReceiptPrefix", SettingValue = "REC-", GroupName = "Payments", Description = "Format prefix added to payment invoice transaction receipts.", LastModified = DateTime.UtcNow },
                new GymSetting { GymID = gym.GymID, SettingKey = "ReminderDaysBeforeExpiration", SettingValue = "3", GroupName = "Membership", Description = "Days ahead of membership expiration to raise alerts or send reminders.", LastModified = DateTime.UtcNow },
                new GymSetting { GymID = gym.GymID, SettingKey = "AllowedImageTypes", SettingValue = ".jpg,.jpeg,.png", GroupName = "Security", Description = "Comma-separated list of approved image file extensions.", LastModified = DateTime.UtcNow },
                new GymSetting { GymID = gym.GymID, SettingKey = "MaxUploadSize", SettingValue = "5242880", GroupName = "Security", Description = "Maximum member photo upload limit size in bytes (e.g. 5MB = 5242880).", LastModified = DateTime.UtcNow },
                new GymSetting { GymID = gym.GymID, SettingKey = "PasswordPolicyRegex", SettingValue = "^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[@$!%*?&])[A-Za-z\\d@$!%*?&]{8,}$", GroupName = "Security", Description = "Regex pattern validating password strength rules.", LastModified = DateTime.UtcNow }
            };

            _dbContext.GymSettings.AddRange(defaultSettings);

            // 4. Create Gym Owner User
            string passwordHash = string.Empty;
            if (!string.IsNullOrWhiteSpace(dto.AdminPassword))
            {
                passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.AdminPassword);
            }

            var user = new User
            {
                GymID = gym.GymID,
                Username = dto.AdminUsername,
                Email = dto.AdminEmail,
                PasswordHash = passwordHash,
                FirstName = dto.AdminFirstName,
                LastName = dto.AdminLastName,
                Role = UserRole.GymOwner,
                IsActive = true,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            var responseDto = new OnboardingResponseDto
            {
                GymID = gym.GymID,
                GymName = gym.Name,
                AdminUserID = user.UserID,
                AdminUsername = user.Username,
                PlanName = plan.Name,
                SubscriptionStatus = subscription.Status.ToString(),
                ExpiresAt = subscription.ExpiresAt
            };

            return Ok(ApiResponse<OnboardingResponseDto>.SuccessResponse(responseDto, "Tenant onboarded successfully."));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return BadRequest(ApiResponse.FailureResponse($"Failed to onboard tenant: {ex.Message}"));
        }
    }

    /// <summary>
    /// Retrieves active subscription details for a specific Gym tenant.
    /// </summary>
    [HttpGet("subscription/{gymId}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<GymSubscriptionDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> GetSubscription(int gymId)
    {
        var subscription = await _dbContext.GymSubscriptions
            .Include(s => s.Plan)
            .Include(s => s.Gym)
            .FirstOrDefaultAsync(s => s.GymID == gymId);

        if (subscription == null)
        {
            return NotFound(ApiResponse.FailureResponse($"Subscription details not found for Gym ID {gymId}."));
        }

        var dto = new GymSubscriptionDto
        {
            SubscriptionID = subscription.SubscriptionID,
            GymID = subscription.GymID,
            GymName = subscription.Gym?.Name ?? "Unknown",
            PlanID = subscription.PlanID,
            PlanName = subscription.Plan?.Name ?? "Unknown",
            Status = subscription.Status.ToString(),
            StartedAt = subscription.StartedAt,
            ExpiresAt = subscription.ExpiresAt,
            TrialEndsAt = subscription.TrialEndsAt
        };

        return Ok(ApiResponse<GymSubscriptionDto>.SuccessResponse(dto, "Subscription info retrieved successfully."));
    }
}
