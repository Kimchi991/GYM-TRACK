using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.API.Services;
using GymTrackPro.API.Authorization;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize(Policy = Policies.ActiveAppUser)]
public class MeController : ControllerBase
{
    private readonly ICurrentUserContext _currentUser;
    private readonly IAuthenticationService _authenticationService;

    public MeController(
        ICurrentUserContext currentUser,
        IAuthenticationService authenticationService)
    {
        _currentUser = currentUser;
        _authenticationService = authenticationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<UserResponseDto>), 200)]
    public async Task<IActionResult> GetCurrentProfile()
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedAccessException("An internally resolved SQL user is required.");
        var firebaseUid = _currentUser.FirebaseUid
            ?? throw new UnauthorizedAccessException("A Firebase identity is required.");
        var profile = await _authenticationService.GetCurrentUserAsync(userId, firebaseUid);

        return Ok(ApiResponse<UserResponseDto>.SuccessResponse(profile, "Current profile retrieved."));
    }
}
