using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IAttendanceService
{
    Task<AttendanceDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResultDto<AttendanceDto>> GetMemberHistoryAsync(
        int memberId,
        DateOnly? fromGymDate,
        DateOnly? endExclusiveGymDate,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceDto>> GetLegacyMemberHistoryAsync(
        int memberId,
        CancellationToken cancellationToken = default);

    // Deprecated compatibility adapters. Remove after 2027-01-12.
    Task<AttendanceDto> CheckInAsync(string qrCode, CancellationToken cancellationToken = default);
    Task CheckOutAsync(int attendanceID, CancellationToken cancellationToken = default);

    Task<AttendanceDto> CheckInAsync(CheckInRequestDto request, CancellationToken cancellationToken = default);
    Task<AttendanceDto> CheckInCurrentMemberAsync(AttendanceOperationRequestDto request, CancellationToken cancellationToken = default);
    Task<AttendanceDto> CheckOutAsync(int attendanceID, CheckOutRequestDto request, CancellationToken cancellationToken = default);
    Task<AttendanceDto> CheckOutCurrentMemberAsync(CheckOutRequestDto request, CancellationToken cancellationToken = default);
    Task<AttendanceDto> CorrectCheckoutAsync(int attendanceID, CorrectCheckoutRequestDto request, CancellationToken cancellationToken = default);
    Task<AttendanceDto> VoidAsync(int attendanceID, VoidAttendanceRequestDto request, CancellationToken cancellationToken = default);

    Task<AttendanceDto?> GetCurrentOpenSessionAsync(CancellationToken cancellationToken = default);
    Task<AttendanceHistoryPageDto> GetAttendanceHistoryAsync(
        DateOnly? fromGymDate,
        DateOnly? endExclusiveGymDate,
        int page = 1,
        int pageSize = 30,
        CancellationToken cancellationToken = default);
    Task<EmergencyEvacuationManifestDto> GetEmergencyEvacuationManifestAsync(CancellationToken cancellationToken = default);
}
