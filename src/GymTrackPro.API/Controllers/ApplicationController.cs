using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Provides public endpoints for submitting membership applications and staff endpoints for verifying payments and approving memberships.
/// </summary>
[ApiController]
[Route("api/v1/applications")]
public class ApplicationController : ControllerBase
{
    private readonly IApplicationService _applicationService;
    private readonly ICurrentUserContext _currentUser;

    public ApplicationController(IApplicationService applicationService, ICurrentUserContext currentUser)
    {
        _applicationService = applicationService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Public submission endpoint to register a new member application (with payment reference).
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ApplicationListItemDto>), 201)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    public async Task<IActionResult> Submit([FromBody] SubmitApplicationDto dto)
    {
        try
        {
            var application = await _applicationService.SubmitApplicationAsync(dto);
            return Created(string.Empty, ApiResponse<ApplicationListItemDto>.SuccessResponse(application, "Application submitted successfully. Please wait for receptionist payment verification."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse.FailureResponse(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.FailureResponse(ex.Message));
        }
    }

    /// <summary>
    /// Retrieves all pending applications for verification. Restricted to Back Office staff.
    /// </summary>
    [HttpGet("pending")]
    [Authorize(Policy = Policies.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ApplicationListItemDto>>), 200)]
    public async Task<IActionResult> GetPending()
    {
        var list = await _applicationService.GetPendingApplicationsAsync();
        return Ok(ApiResponse<IEnumerable<ApplicationListItemDto>>.SuccessResponse(list));
    }

    /// <summary>
    /// Verifies the payment of a pending application and either Approves or Rejects it. Restricted to Back Office staff.
    /// </summary>
    [HttpPost("{id:int}/verify")]
    [Authorize(Policy = Policies.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<ApplicationListItemDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> Verify(int id, [FromBody] VerifyApplicationDto dto)
    {
        var actorUserId = _currentUser.UserId;
        if (!actorUserId.HasValue || actorUserId.Value <= 0)
        {
            return Unauthorized(ApiResponse.FailureResponse("An active staff user session is required."));
        }

        try
        {
            var result = await _applicationService.VerifyApplicationAsync(id, actorUserId.Value, dto);
            var message = dto.Status == GymTrackPro.Shared.Enums.ApplicationStatus.Approved
                ? "Application approved successfully, membership activated, and verification email sent."
                : "Application rejected successfully.";
            return Ok(ApiResponse<ApplicationListItemDto>.SuccessResponse(result, message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse.FailureResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.FailureResponse(ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.FailureResponse(ex.Message));
        }
    }
}
