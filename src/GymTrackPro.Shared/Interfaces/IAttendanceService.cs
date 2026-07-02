using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IAttendanceService
{
    Task<AttendanceDto?> GetByIdAsync(int id);
    Task<IEnumerable<AttendanceDto>> GetByMemberIdAsync(int memberId);
    Task<AttendanceDto> CheckInAsync(string qrCode);
    Task CheckOutAsync(int attendanceID);
}
