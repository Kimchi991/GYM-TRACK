using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Authentication;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;

    public AuthController(IAuthenticationService authService)
    {
        _authService = authService;
    }

    [HttpPost("sync-user")]
    [Authorize(Policy = Policies.FirebaseOnboarding)]
    [EnableRateLimiting("Auth")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), 200)]
    public async Task<IActionResult> SyncUser()
    {
        if (!FirebaseClaimTypes.TryGetVerifiedIdentity(User, out var uid, out var email))
        {
            return Unauthorized(ApiResponse.FailureResponse(
                "A valid Firebase identity is required.",
                "FIREBASE_IDENTITY_INVALID"));
        }

        var response = await _authService.SyncUserAsync(uid, email);
        return Ok(ApiResponse<UserResponseDto>.SuccessResponse(response, "User synchronized successfully."));
    }

    [HttpPost("activate")]
    [Authorize(Policy = Policies.FirebaseOnboarding)]
    [EnableRateLimiting("Activation")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), 200)]
    public async Task<IActionResult> ActivateApp([FromBody] ActivateInviteDto request)
    {
        if (!FirebaseClaimTypes.TryGetVerifiedIdentity(User, out var uid, out var email))
        {
            return Unauthorized(ApiResponse.FailureResponse(
                "A valid Firebase identity is required.",
                "FIREBASE_IDENTITY_INVALID"));
        }

        var response = await _authService.ActivateAppAsync(uid, email, request);
        return Ok(ApiResponse<UserResponseDto>.SuccessResponse(response, "App activated successfully."));
    }
}
