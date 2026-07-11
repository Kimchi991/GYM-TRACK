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

    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 401)]
    public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
    {
        try
        {
            var response = await _authService.AuthenticateAsync(loginDto);
            if (response == null)
            {
                return Unauthorized(ApiResponse.FailureResponse("Invalid username or password."));
            }
            return Ok(ApiResponse<UserResponseDto>.SuccessResponse(response, "Login successful."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.FailureResponse(ex.Message));
        }
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), 201)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    public async Task<IActionResult> Register([FromBody] RegisterUserDto registerDto)
    {
        try
        {
            var response = await _authService.RegisterAsync(registerDto);
            return Ok(ApiResponse<UserResponseDto>.SuccessResponse(response, "Registration successful."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.FailureResponse(ex.Message));
        }
    }

    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    public async Task<IActionResult> ForgotPassword([FromBody] string email)
    {
        await _authService.ForgotPasswordAsync(email);
        return Ok(ApiResponse.SuccessResponse("If the email matches an active account, a password reset link has been sent."));
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto resetDto)
    {
        var success = await _authService.ResetPasswordAsync(resetDto);
        if (!success)
        {
            return BadRequest(ApiResponse.FailureResponse("Invalid or expired password reset token."));
        }
        return Ok(ApiResponse.SuccessResponse("Password has been reset successfully."));
    }

    [HttpPost("verify-email")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 400)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto verifyDto)
    {
        var success = await _authService.VerifyEmailAsync(verifyDto.Email, verifyDto.Token);
        if (!success)
        {
            return BadRequest(ApiResponse.FailureResponse("Invalid or expired email verification token."));
        }
        return Ok(ApiResponse.SuccessResponse("Email has been verified successfully. You may now log in."));
    }
}
