using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.API.Services;
using GymTrackPro.API.Authorization;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ICurrentUserContext _currentUser;

    public UsersController(IAuthenticationService authService, ICurrentUserContext currentUser)
    {
        _authService = authService;
        _currentUser = currentUser;
    }

    [HttpPost("{userId}/app-invite")]
    [Authorize(Policy = Policies.OwnerOnly)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(ApiResponse<AppInviteCodeResponseDto>), 200)]
    public async Task<IActionResult> CreateUserInvite(int userId, [FromBody] CreateAppInviteDto dto)
    {
        var response = await _authService.CreateUserInviteAsync(userId, GetRequiredActorUserId(), dto);
        return Ok(ApiResponse<AppInviteCodeResponseDto>.SuccessResponse(response, "User invite created."));
    }

    [HttpGet("{userId}/app-invite/status")]
    [Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType(typeof(ApiResponse<AppInviteResponseDto>), 200)]
    public async Task<IActionResult> GetUserInviteStatus(int userId)
    {
        var response = await _authService.GetUserInviteStatusAsync(userId);
        return Ok(ApiResponse<AppInviteResponseDto>.SuccessResponse(response, "User invite status retrieved."));
    }

    [HttpDelete("{userId}/app-invite")]
    [Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> RevokeUserInvite(int userId)
    {
        await _authService.RevokeUserInviteAsync(userId, GetRequiredActorUserId());
        return Ok(ApiResponse<object>.SuccessResponse(new object(), "User invite revoked."));
    }

    private int GetRequiredActorUserId() => _currentUser.UserId
        ?? throw new UnauthorizedAccessException("An internally resolved SQL user is required.");
}
