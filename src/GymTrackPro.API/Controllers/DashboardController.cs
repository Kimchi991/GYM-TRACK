using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.API.Authorization;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Exposes real-time operations, membership, and financial analytics for GymTrackPro.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = Policies.BackOffice)]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Retrieves current key metrics, including active attendees, memberships, revenue figures, and plan performance.
    /// </summary>
    /// <returns>A standardized API response containing the dashboard metrics.</returns>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(ApiResponse<DashboardMetricsDto>), 200)]
    public async Task<IActionResult> GetMetrics()
    {
        var metrics = await _dashboardService.GetDashboardMetricsAsync();
        return Ok(ApiResponse<DashboardMetricsDto>.SuccessResponse(metrics, "Dashboard metrics retrieved successfully."));
    }
}
