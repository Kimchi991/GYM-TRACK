using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymTrackPro.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AttendanceController : ControllerBase
{
    private const string LegacySunset = "Tue, 12 Jan 2027 00:00:00 GMT";
    private readonly IAttendanceService _attendanceService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<AttendanceController> _logger;

    public AttendanceController(
        IAttendanceService attendanceService,
        ICurrentUserContext currentUser,
        ILogger<AttendanceController> logger)
    {
        _attendanceService = attendanceService;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var attendance = await _attendanceService.GetByIdAsync(id, cancellationToken);
        return attendance is null
            ? NotFound(ApiResponse.FailureResponse("Attendance was not found.", "ATTENDANCE_NOT_FOUND"))
            : Ok(ApiResponse<AttendanceDto>.SuccessResponse(attendance));
    }

    [Obsolete("Use GET /api/v1/attendance/member/{memberId}/history. Remove after 2027-01-12.")]
    [HttpGet("member/{memberId:int}")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<IActionResult> GetByMemberId(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        AddLegacyDeprecationHeaders($"/api/v1/attendance/member/{memberId}/history");
        LogLegacyRouteUsage("GET /api/v1/attendance/member/{memberId}");
        var result = await _attendanceService.GetLegacyMemberHistoryAsync(
            memberId,
            cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<AttendanceDto>>.SuccessResponse(result));
    }

    [HttpGet("member/{memberId:int}/history")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<IActionResult> GetMemberHistory(
        int memberId,
        [FromQuery(Name = "from")] DateOnly? fromGymDate,
        [FromQuery(Name = "to")] DateOnly? endExclusiveGymDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _attendanceService.GetMemberHistoryAsync(
            memberId,
            fromGymDate,
            endExclusiveGymDate,
            page,
            pageSize,
            cancellationToken);
        return Ok(ApiResponse<PagedResultDto<AttendanceDto>>.SuccessResponse(result));
    }

    [Obsolete("Use POST /api/v1/attendance/check-in. Remove after 2027-01-12.")]
    [HttpPost("checkin")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<IActionResult> CheckInLegacy(
        [FromBody] string qrCode,
        CancellationToken cancellationToken)
    {
        AddLegacyDeprecationHeaders("/api/v1/attendance/check-in");
        LogLegacyRouteUsage("POST /api/v1/attendance/checkin");
        var attendance = await _attendanceService.CheckInAsync(qrCode, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = attendance.AttendanceID },
            ApiResponse<AttendanceDto>.SuccessResponse(attendance, "Checked in successfully."));
    }

    [Obsolete("Use POST /api/v1/attendance/{id}/check-out. Remove after 2027-01-12.")]
    [HttpPost("{id:int}/checkout")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<IActionResult> CheckOutLegacy(int id, CancellationToken cancellationToken)
    {
        AddLegacyDeprecationHeaders($"/api/v1/attendance/{id}/check-out");
        LogLegacyRouteUsage("POST /api/v1/attendance/{id}/checkout");
        await _attendanceService.CheckOutAsync(id, cancellationToken);
        return Ok(ApiResponse.SuccessResponse("Checked out successfully."));
    }

    [HttpPost("check-in")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<IActionResult> CheckIn(
        [FromBody] CheckInRequestDto request,
        CancellationToken cancellationToken)
    {
        var attendance = await _attendanceService.CheckInAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = attendance.AttendanceID },
            ApiResponse<AttendanceDto>.SuccessResponse(attendance, "Checked in successfully."));
    }

    [HttpPost("{id:int}/check-out")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<IActionResult> CheckOut(
        int id,
        [FromBody] CheckOutRequestDto request,
        CancellationToken cancellationToken)
    {
        var attendance = await _attendanceService.CheckOutAsync(id, request, cancellationToken);
        return Ok(ApiResponse<AttendanceDto>.SuccessResponse(attendance, "Checked out successfully."));
    }

    [HttpPost("{id:int}/correct-checkout")]
    [Authorize(Policy = Policies.OwnerOnly)]
    public async Task<IActionResult> CorrectCheckout(
        int id,
        [FromBody] CorrectCheckoutRequestDto request,
        CancellationToken cancellationToken)
    {
        var attendance = await _attendanceService.CorrectCheckoutAsync(id, request, cancellationToken);
        return Ok(ApiResponse<AttendanceDto>.SuccessResponse(attendance, "Corrected checkout successfully."));
    }

    [HttpPost("{id:int}/void")]
    [Authorize(Policy = Policies.OwnerOnly)]
    public async Task<IActionResult> Void(
        int id,
        [FromBody] VoidAttendanceRequestDto request,
        CancellationToken cancellationToken)
    {
        var attendance = await _attendanceService.VoidAsync(id, request, cancellationToken);
        return Ok(ApiResponse<AttendanceDto>.SuccessResponse(attendance, "Voided attendance successfully."));
    }

    [HttpGet("emergency-manifest")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<IActionResult> GetEmergencyEvacuationManifest(CancellationToken cancellationToken)
    {
        var manifest = await _attendanceService.GetEmergencyEvacuationManifestAsync(cancellationToken);
        return Ok(ApiResponse<EmergencyEvacuationManifestDto>.SuccessResponse(manifest, "Emergency evacuation manifest generated successfully."));
    }

    private void AddLegacyDeprecationHeaders(string successorPath)
    {
        Response.Headers["Deprecation"] = "true";
        Response.Headers["Sunset"] = LegacySunset;
        Response.Headers["Link"] = $"<{successorPath}>; rel=\"successor-version\"";
    }

    private void LogLegacyRouteUsage(string routeTemplate)
    {
        _logger.LogWarning(
            "Deprecated attendance route used. RouteTemplate: {RouteTemplate}; ActorUserId: {ActorUserId}; CorrelationId: {CorrelationId}",
            routeTemplate,
            _currentUser.UserId,
            HttpContext.TraceIdentifier);
    }
}
