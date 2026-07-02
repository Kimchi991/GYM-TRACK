using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.API.Repositories;

public class AttendanceRepository : BaseRepository<Attendance>, IAttendanceRepository
{
    public AttendanceRepository(GymDbContext context) : base(context)
    {
    }

    public override async Task<Attendance?> GetByIdAsync(int id)
    {
        return await _dbSet
            .Include(a => a.Member)
            .FirstOrDefaultAsync(a => a.AttendanceID == id);
    }

    public async Task<IEnumerable<Attendance>> GetByMemberIdAsync(int memberId)
    {
        return await _dbSet
            .Where(a => a.MemberID == memberId)
            .ToListAsync();
    }

    public async Task<IEnumerable<Attendance>> GetByDateAsync(DateTime date)
    {
        var targetDate = date.Date;
        return await _dbSet
            .Include(a => a.Member)
            .Where(a => a.AttendanceDate.Date == targetDate)
            .ToListAsync();
    }
}
