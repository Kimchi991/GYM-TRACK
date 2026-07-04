using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;

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
    [Authorize]
    [EnableRateLimiting("Auth")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), 200)]
    public async Task<IActionResult> SyncUser()
    {
        var uidClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "user_id");
        var emailClaim = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == "email");

        if (uidClaim == null || emailClaim == null)
        {
            return Unauthorized(ApiResponse.FailureResponse("Invalid Firebase token: Missing UID or Email."));
        }

        var response = await _authService.SyncUserAsync(uidClaim.Value, emailClaim.Value);
        return Ok(ApiResponse<UserResponseDto>.SuccessResponse(response, "User synchronized successfully."));
    }
}
