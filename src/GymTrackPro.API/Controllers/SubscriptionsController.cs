using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Provides endpoints for managing member subscriptions and pauses.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionsController(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    /// <summary>
    /// Retrieves a specific subscription record by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the subscription.</param>
    /// <returns>A standardized API response containing the subscription details.</returns>
    /// <response code="200">If the subscription is found.</response>
    /// <response code="404">If the subscription record does not exist.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 404)]
    public async Task<IActionResult> GetById(int id)
    {
        var subscription = await _subscriptionService.GetByIdAsync(id);
        if (subscription == null)
        {
            return NotFound(ApiResponse.FailureResponse("Subscription record not found."));
        }
        return Ok(ApiResponse<SubscriptionResponseDto>.SuccessResponse(subscription));
    }

    /// <summary>
    /// Retrieves all subscriptions linked to a specific member.
    /// </summary>
    /// <param name="memberId">The unique identifier of the member.</param>
    /// <returns>A standardized API response listing the member's subscriptions.</returns>
    [HttpGet("member/{memberId:int}")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<SubscriptionResponseDto>>), 200)]
    public async Task<IActionResult> GetByMemberId(int memberId)
    {
        var subscriptions = await _subscriptionService.GetByMemberIdAsync(memberId);
        return Ok(ApiResponse<IEnumerable<SubscriptionResponseDto>>.SuccessResponse(subscriptions));
    }

    /// <summary>
    /// Enrolls a member in a new subscription plan.
    /// </summary>
    /// <param name="subscribeDto">The parameters specifying the member and the plan.</param>
    /// <returns>A standardized API response containing the newly created subscription details.</returns>
    /// <response code="201">If the subscription is successfully registered.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponseDto>), 201)]
    public async Task<IActionResult> Subscribe([FromBody] CreateSubscriptionDto subscribeDto)
    {
        var subscription = await _subscriptionService.SubscribeMemberAsync(subscribeDto);
        return CreatedAtAction(nameof(GetById), new { id = subscription.SubscriptionID }, ApiResponse<SubscriptionResponseDto>.SuccessResponse(subscription, "Subscription created successfully."));
    }

    /// <summary>
    /// Temporarily pauses an active member subscription.
    /// </summary>
    /// <param name="id">The unique identifier of the subscription to pause.</param>
    /// <param name="reason">The reason for pausing the membership.</param>
    /// <returns>A standardized API response confirming the pause operation.</returns>
    /// <response code="200">If the subscription was successfully paused.</response>
    [HttpPost("{id:int}/pause")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    public async Task<IActionResult> Pause(int id, [FromBody] PauseSubscriptionDto pauseDto)
    {
        await _subscriptionService.PauseSubscriptionAsync(id, pauseDto.Reason);
        return Ok(ApiResponse.SuccessResponse("Subscription paused successfully."));
    }

    /// <summary>
    /// Resumes a previously paused subscription.
    /// </summary>
    /// <param name="id">The unique identifier of the subscription to resume.</param>
    /// <returns>A standardized API response confirming the resume operation.</returns>
    /// <response code="200">If the subscription was successfully resumed.</response>
    [HttpPost("{id:int}/resume")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    public async Task<IActionResult> Resume(int id)
    {
        await _subscriptionService.ResumeSubscriptionAsync(id);
        return Ok(ApiResponse.SuccessResponse("Subscription resumed successfully."));
    }

    /// <summary>
    /// Renews an active or expired subscription with a payment atomically inside a transaction.
    /// </summary>
    /// <param name="renewDto">The subscription details and payment inputs.</param>
    /// <returns>A standardized API response containing the new subscription details.</returns>
    /// <response code="200">If the renewal was successful.</response>
    [HttpPost("renew")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionResponseDto>), 200)]
    public async Task<IActionResult> Renew([FromBody] RenewSubscriptionDto renewDto)
    {
        var subscription = await _subscriptionService.RenewSubscriptionAsync(renewDto);
        return Ok(ApiResponse<SubscriptionResponseDto>.SuccessResponse(subscription, "Subscription renewed successfully with payment."));
    }
}
