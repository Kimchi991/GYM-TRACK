using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1/me/dashboard")]
[Authorize(Policy = Policies.GymGoerSelf)]
public class MeDashboardController : ControllerBase
{
    private readonly IGymGoerProjectionService _projectionService;

    public MeDashboardController(IGymGoerProjectionService projectionService)
    {
        _projectionService = projectionService;
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        var result = await _projectionService.GetGoerDashboardAsync(cancellationToken);
        return Ok(ApiResponse<GoerDashboardDto>.SuccessResponse(result));
    }

    [HttpGet("/api/v1/me/digital-card")]
    public async Task<IActionResult> GetDigitalCard(CancellationToken cancellationToken)
    {
        var result = await _projectionService.GetDigitalCardAsync(cancellationToken);
        return Ok(ApiResponse<GoerDigitalCardDto>.SuccessResponse(result));
    }
}
