using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class MembershipPlanService : IMembershipPlanService
{
    private readonly IMembershipPlanRepository _planRepository;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MembershipPlanService(
        IMembershipPlanRepository planRepository,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor)
    {
        _planRepository = planRepository;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
    }

    private int? GetCurrentUserId()
    {
        var nameIdentifier = _httpContextAccessor.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(nameIdentifier, out int userId))
        {
            return userId;
        }
        return null;
    }

    private string GetClientIpAddress()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    public async Task<MembershipPlanResponseDto?> GetByIdAsync(int id)
    {
        var plan = await _planRepository.GetByIdAsync(id);
        if (plan == null) return null;

        return MapToDto(plan);
    }

    public async Task<IEnumerable<MembershipPlanResponseDto>> GetAllAsync()
    {
        var plans = await _planRepository.GetAllAsync();
        return plans.Select(MapToDto);
    }

    public async Task<MembershipPlanResponseDto> CreatePlanAsync(CreateMembershipPlanDto createDto)
    {
        // Enforce name uniqueness
        var existing = await _planRepository.GetByNameAsync(createDto.PlanName);
        if (existing != null)
        {
            throw new ArgumentException("A membership plan with this name already exists.");
        }

        var plan = new MembershipPlan
        {
            PlanName = createDto.PlanName,
            DurationDays = createDto.DurationDays,
            Price = createDto.Price,
            Description = createDto.Description,
            Status = "Active",
            LastModified = DateTime.UtcNow
        };

        await _planRepository.AddAsync(plan);

        // Log audit log
        await _auditService.LogActivityAsync(GetCurrentUserId(), "Plan Created", $"Membership plan '{plan.PlanName}' (ID: {plan.PlanID}) created with Price: {plan.Price:C}, Duration: {plan.DurationDays} days.", GetClientIpAddress());

        return MapToDto(plan);
    }

    public async Task<MembershipPlanResponseDto> UpdatePlanAsync(int id, CreateMembershipPlanDto updateDto)
    {
        var plan = await _planRepository.GetByIdAsync(id);
        if (plan == null)
        {
            throw new KeyNotFoundException("Membership plan not found.");
        }

        // Enforce name uniqueness
        var existing = await _planRepository.GetByNameAsync(updateDto.PlanName);
        if (existing != null && existing.PlanID != id)
        {
            throw new ArgumentException("A membership plan with this name already exists.");
        }

        var oldName = plan.PlanName;
        plan.PlanName = updateDto.PlanName;
        plan.DurationDays = updateDto.DurationDays;
        plan.Price = updateDto.Price;
        plan.Description = updateDto.Description;
        plan.LastModified = DateTime.UtcNow;

        await _planRepository.UpdateAsync(plan);

        // Log audit log
        await _auditService.LogActivityAsync(GetCurrentUserId(), "Plan Updated", $"Membership plan '{oldName}' (ID: {plan.PlanID}) updated. New Name: '{plan.PlanName}', Price: {plan.Price:C}.", GetClientIpAddress());

        return MapToDto(plan);
    }

    public async Task DeletePlanAsync(int id)
    {
        var plan = await _planRepository.GetByIdAsync(id);
        if (plan != null)
        {
            plan.Status = "Inactive";
            plan.LastModified = DateTime.UtcNow;
            await _planRepository.UpdateAsync(plan);

            // Log audit log
            await _auditService.LogActivityAsync(GetCurrentUserId(), "Plan Deleted", $"Membership plan '{plan.PlanName}' (ID: {plan.PlanID}) marked Inactive.", GetClientIpAddress());
        }
    }

    private static MembershipPlanResponseDto MapToDto(MembershipPlan plan)
    {
        return new MembershipPlanResponseDto
        {
            PlanID = plan.PlanID,
            PlanName = plan.PlanName,
            DurationDays = plan.DurationDays,
            Price = plan.Price,
            Description = plan.Description,
            Status = plan.Status,
            LastModified = plan.LastModified
        };
    }
}
