using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.API.Repositories;

public class MembershipPlanRepository : BaseRepository<MembershipPlan>, IMembershipPlanRepository
{
    public MembershipPlanRepository(GymDbContext context) : base(context)
    {
    }

    public async Task<MembershipPlan?> GetByNameAsync(string name)
    {
        return await _dbSet.FirstOrDefaultAsync(p => p.PlanName == name);
    }
}
