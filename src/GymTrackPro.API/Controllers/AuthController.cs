using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Provides endpoints for user authentication, registration, password resets, and email verification.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;

    public AuthController(IAuthenticationService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Authenticates a user and returns a JWT access token.
    /// </summary>
    /// <param name="loginDto">The user credentials (username and password).</param>
    /// <returns>A standardized API response containing the user profile and JWT access token.</returns>
    /// <response code="200">If login was successful.</response>
    /// <response code="401">If the credentials are invalid or account is locked.</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 401)]
    public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
    {
        var response = await _authService.AuthenticateAsync(loginDto);
        if (response == null)
        {
            return Unauthorized(ApiResponse.FailureResponse("Invalid username or password."));
        }
        return Ok(ApiResponse<UserResponseDto>.SuccessResponse(response, "Login successful."));
    }

    /// <summary>
    /// Registers a new user account.
    /// </summary>
    /// <param name="registerDto">The details of the user to register.</param>
    /// <returns>A standardized API response containing the newly created user profile.</returns>
    /// <response code="201">If registration was successful.</response>
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), 201)]
    public async Task<IActionResult> Register([FromBody] RegisterUserDto registerDto)
    {
        var response = await _authService.RegisterAsync(registerDto);
        return CreatedAtAction(nameof(Login), ApiResponse<UserResponseDto>.SuccessResponse(response, "Registration successful."));
    }

    /// <summary>
    /// Initiates a stateful forgot password request by sending an email with a reset token.
    /// </summary>
    /// <param name="email">The email address of the user who forgot their password.</param>
    /// <returns>A standardized API response confirming the email dispatch.</returns>
    /// <response code="200">If the action was processed.</response>
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    public async Task<IActionResult> ForgotPassword([FromBody] string email)
    {
        await _authService.ForgotPasswordAsync(email);
        return Ok(ApiResponse.SuccessResponse("If the email matches an active account, a password reset link has been sent."));
    }

    /// <summary>
    /// Resets a user's password using a valid reset token.
    /// </summary>
    /// <param name="resetDto">The password reset parameters containing the email, token, and new password.</param>
    /// <returns>A standardized API response confirming password reset success.</returns>
    /// <response code="200">If the password was successfully reset.</response>
    /// <response code="400">If the token was invalid or expired.</response>
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

    /// <summary>
    /// Verifies a user's email address using a valid verification token.
    /// </summary>
    /// <param name="email">The email address to verify.</param>
    /// <param name="token">The verification token sent via email.</param>
    /// <returns>A standardized API response confirming email verification success.</returns>
    /// <response code="200">If the email was successfully verified.</response>
    /// <response code="400">If the token was invalid.</response>
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
