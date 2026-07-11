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
/// Exposes platform-level administrative capabilities for managing tenants and subscriptions.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "PlatformAdmin")]
public class PlatformAdminController : ControllerBase
{
    private readonly GymDbContext _dbContext;

    public PlatformAdminController(GymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Lists all registered gym tenants in the SaaS platform.
    /// </summary>
    [HttpGet("tenants")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    public async Task<IActionResult> ListTenants()
    {
        var tenants = await _dbContext.Gyms
            .IgnoreQueryFilters()
            .Select(g => new
            {
                g.GymID,
                g.Name,
                g.Address,
                g.ContactNumber,
                g.Capacity,
                g.CreatedAt,
                g.IsDeleted,
                Subscription = _dbContext.GymSubscriptions
                    .IgnoreQueryFilters()
                    .Where(s => s.GymID == g.GymID)
                    .Select(s => new
                    {
                        s.SubscriptionID,
                        s.PlanID,
                        PlanName = s.Plan != null ? s.Plan.Name : "Unknown",
                        Status = s.Status.ToString(),
                        s.StartedAt,
                        s.ExpiresAt,
                        s.TrialEndsAt
                    })
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.SuccessResponse(tenants, "Registered tenants retrieved successfully."));
    }

    /// <summary>
    /// Updates the subscription details for a specific tenant.
    /// </summary>
    [HttpPut("tenants/{gymId}/subscription")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> UpdateSubscription(int gymId, [FromBody] UpdateTenantSubscriptionDto dto)
    {
        var gymExists = await _dbContext.Gyms.IgnoreQueryFilters().AnyAsync(g => g.GymID == gymId);
        if (!gymExists)
        {
            return NotFound(ApiResponse.FailureResponse($"Gym with ID {gymId} does not exist."));
        }

        var planExists = await _dbContext.SubscriptionPlans.AnyAsync(p => p.PlanID == dto.PlanID);
        if (!planExists)
        {
            return BadRequest(ApiResponse.FailureResponse($"Subscription plan with ID {dto.PlanID} does not exist."));
        }

        if (!Enum.TryParse<SubscriptionStatus>(dto.Status, true, out var status))
        {
            return BadRequest(ApiResponse.FailureResponse($"Invalid subscription status: {dto.Status}."));
        }

        var subscription = await _dbContext.GymSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.GymID == gymId);

        if (subscription == null)
        {
            subscription = new GymSubscription
            {
                GymID = gymId,
                PlanID = dto.PlanID,
                Status = status,
                StartedAt = DateTime.UtcNow,
                ExpiresAt = dto.ExpiresAt ?? DateTime.UtcNow.AddMonths(1)
            };
            _dbContext.GymSubscriptions.Add(subscription);
        }
        else
        {
            subscription.PlanID = dto.PlanID;
            subscription.Status = status;
            if (dto.ExpiresAt.HasValue)
            {
                subscription.ExpiresAt = dto.ExpiresAt.Value;
            }
            _dbContext.Entry(subscription).State = EntityState.Modified;
        }

        await _dbContext.SaveChangesAsync();
        return Ok(ApiResponse.SuccessResponse("Subscription updated successfully."));
    }

    /// <summary>
    /// Suspends a gym tenant subscription.
    /// </summary>
    [HttpPost("tenants/{gymId}/suspend")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> SuspendTenant(int gymId)
    {
        var subscription = await _dbContext.GymSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.GymID == gymId);

        if (subscription == null)
        {
            return NotFound(ApiResponse.FailureResponse($"Subscription details not found for Gym ID {gymId}."));
        }

        subscription.Status = SubscriptionStatus.Suspended;
        _dbContext.Entry(subscription).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();

        return Ok(ApiResponse.SuccessResponse("Tenant subscription suspended successfully."));
    }

    /// <summary>
    /// Unsuspends a gym tenant subscription, returning it to Active.
    /// </summary>
    [HttpPost("tenants/{gymId}/unsuspend")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> UnsuspendTenant(int gymId)
    {
        var subscription = await _dbContext.GymSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.GymID == gymId);

        if (subscription == null)
        {
            return NotFound(ApiResponse.FailureResponse($"Subscription details not found for Gym ID {gymId}."));
        }

        subscription.Status = SubscriptionStatus.Active;
        _dbContext.Entry(subscription).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();

        return Ok(ApiResponse.SuccessResponse("Tenant subscription unsuspended successfully."));
    }
}

public class UpdateTenantSubscriptionDto
{
    public int PlanID { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}
