using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.API.Authorization;
using GymTrackPro.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1/me/attendance")]
[Authorize(Policy = Policies.GymGoerSelf)]
public class MeAttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendanceService;

    public MeAttendanceController(IAttendanceService attendanceService)
    {
        _attendanceService = attendanceService;
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory(
        [FromQuery(Name = "from")] DateOnly? fromGymDate,
        [FromQuery(Name = "to")] DateOnly? endExclusiveGymDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _attendanceService.GetAttendanceHistoryAsync(
            fromGymDate,
            endExclusiveGymDate,
            page,
            pageSize,
            cancellationToken);
        return Ok(ApiResponse<AttendanceHistoryPageDto>.SuccessResponse(result));
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentSession(CancellationToken cancellationToken)
    {
        var attendance = await _attendanceService.GetCurrentOpenSessionAsync(cancellationToken);
        var state = new CurrentAttendanceStateDto
        {
            State = attendance is null
                ? AttendanceSessionState.CheckedOut
                : AttendanceSessionState.CheckedIn,
            Session = attendance
        };
        return Ok(ApiResponse<CurrentAttendanceStateDto>.SuccessResponse(state));
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> CheckOutCurrentSession(
        [FromBody] CheckOutRequestDto request,
        CancellationToken cancellationToken)
    {
        var attendance = await _attendanceService.CheckOutCurrentMemberAsync(request, cancellationToken);
        return Ok(ApiResponse<AttendanceDto>.SuccessResponse(attendance, "Checked out successfully."));
    }
}
