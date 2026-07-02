using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Shared.Interfaces;

public interface IAttendanceRepository : IBaseRepository<Attendance>
{
    Task<IEnumerable<Attendance>> GetByMemberIdAsync(int memberId);
    Task<IEnumerable<Attendance>> GetByDateAsync(DateTime date);
}
