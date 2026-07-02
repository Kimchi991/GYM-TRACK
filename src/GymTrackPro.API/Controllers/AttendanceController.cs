using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _attendanceService;

    public AttendanceController(IAttendanceService attendanceService)
    {
        _attendanceService = attendanceService;
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var log = await _attendanceService.GetByIdAsync(id);
        if (log == null)
        {
            return NotFound(ApiResponse.FailureResponse("Attendance log not found."));
        }
        return Ok(ApiResponse<AttendanceDto>.SuccessResponse(log));
    }

    [HttpGet("member/{memberId:int}")]
    public async Task<IActionResult> GetByMemberId(int memberId)
    {
        var logs = await _attendanceService.GetByMemberIdAsync(memberId);
        return Ok(ApiResponse<IEnumerable<AttendanceDto>>.SuccessResponse(logs));
    }

    [HttpPost("checkin")]
    public async Task<IActionResult> CheckIn([FromBody] string qrCode)
    {
        var log = await _attendanceService.CheckInAsync(qrCode);
        return CreatedAtAction(nameof(GetById), new { id = log.AttendanceID }, ApiResponse<AttendanceDto>.SuccessResponse(log, "Checked in successfully."));
    }

    [HttpPost("{id:int}/checkout")]
    public async Task<IActionResult> CheckOut(int id)
    {
        await _attendanceService.CheckOutAsync(id);
        return Ok(ApiResponse.SuccessResponse("Checked out successfully."));
    }
}
