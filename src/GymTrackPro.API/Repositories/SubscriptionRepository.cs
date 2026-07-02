using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.API.Repositories;

public class SubscriptionRepository : BaseRepository<Subscription>, ISubscriptionRepository
{
    public SubscriptionRepository(GymDbContext context) : base(context)
    {
    }

    public override async Task<Subscription?> GetByIdAsync(int id)
    {
        return await _dbSet
            .Include(s => s.Member)
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.SubscriptionID == id);
    }

    public async Task<IEnumerable<Subscription>> GetByMemberIdAsync(int memberId)
    {
        return await _dbSet
            .Include(s => s.Plan)
            .Where(s => s.MemberID == memberId)
            .ToListAsync();
    }
}
