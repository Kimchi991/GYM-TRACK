using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.API.Authorization;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Provides endpoints for managing gym membership plans.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = Policies.BackOffice)]
public class PlansController : ControllerBase
{
    private readonly IMembershipPlanService _planService;

    public PlansController(IMembershipPlanService planService)
    {
        _planService = planService;
    }

    /// <summary>
    /// Retrieves all registered membership plans.
    /// </summary>
    /// <returns>A standardized API response listing available plans.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<MembershipPlanResponseDto>>), 200)]
    public async Task<IActionResult> GetAll()
    {
        var plans = await _planService.GetAllAsync();
        return Ok(ApiResponse<IEnumerable<MembershipPlanResponseDto>>.SuccessResponse(plans));
    }

    /// <summary>
    /// Retrieves details of a specific membership plan by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the membership plan.</param>
    /// <returns>A standardized API response containing the plan details.</returns>
    /// <response code="200">If the plan is found.</response>
    /// <response code="404">If the plan does not exist.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<MembershipPlanResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> GetById(int id)
    {
        var plan = await _planService.GetByIdAsync(id);
        if (plan == null)
        {
            return NotFound(ApiResponse.FailureResponse("Membership plan not found."));
        }
        return Ok(ApiResponse<MembershipPlanResponseDto>.SuccessResponse(plan));
    }

    /// <summary>
    /// Creates a new membership plan. Restricted to Administrators.
    /// </summary>
    /// <param name="createDto">The parameters defining the new plan.</param>
    /// <returns>A standardized API response containing the created plan details.</returns>
    /// <response code="201">If the plan was created successfully.</response>
    [HttpPost]
    [Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType(typeof(ApiResponse<MembershipPlanResponseDto>), 201)]
    public async Task<IActionResult> Create([FromBody] CreateMembershipPlanDto createDto)
    {
        var plan = await _planService.CreatePlanAsync(createDto);
        return CreatedAtAction(nameof(GetById), new { id = plan.PlanID }, ApiResponse<MembershipPlanResponseDto>.SuccessResponse(plan, "Membership plan created successfully."));
    }

    /// <summary>
    /// Updates details of an existing membership plan. Restricted to Administrators.
    /// </summary>
    /// <param name="id">The unique identifier of the plan to update.</param>
    /// <param name="updateDto">The updated plan specifications.</param>
    /// <returns>A standardized API response containing the updated plan details.</returns>
    /// <response code="200">If the plan was successfully updated.</response>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType(typeof(ApiResponse<MembershipPlanResponseDto>), 200)]
    public async Task<IActionResult> Update(int id, [FromBody] CreateMembershipPlanDto updateDto)
    {
        var plan = await _planService.UpdatePlanAsync(id, updateDto);
        return Ok(ApiResponse<MembershipPlanResponseDto>.SuccessResponse(plan, "Membership plan updated successfully."));
    }

    /// <summary>
    /// Deletes a membership plan. Restricted to Administrators.
    /// </summary>
    /// <param name="id">The unique identifier of the plan to delete.</param>
    /// <returns>A standardized API response confirming the deletion.</returns>
    /// <response code="200">If the plan was successfully deleted.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    public async Task<IActionResult> Delete(int id)
    {
        await _planService.DeletePlanAsync(id);
        return Ok(ApiResponse.SuccessResponse("Membership plan deleted successfully."));
    }
}
