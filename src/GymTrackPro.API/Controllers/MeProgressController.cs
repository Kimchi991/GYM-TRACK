using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1/me/progress")]
[Authorize(Policy = Policies.GymGoerSelf)]
public class MeProgressController : ControllerBase
{
    private readonly IGymGoerProjectionService _projectionService;

    public MeProgressController(IGymGoerProjectionService projectionService)
    {
        _projectionService = projectionService;
    }

    [HttpGet]
    public async Task<IActionResult> GetProgress(
        [FromQuery] string? month,
        CancellationToken cancellationToken)
    {
        var selectedMonth = string.IsNullOrWhiteSpace(month)
            ? string.Empty
            : month;
        var result = await _projectionService.GetProgressAsync(selectedMonth, cancellationToken);
        return Ok(ApiResponse<GoerProgressDto>.SuccessResponse(result));
    }
}
