using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Repositories;

public class AccountInviteRepository : BaseRepository<AccountInvite>, IAccountInviteRepository
{
    public AccountInviteRepository(GymDbContext context) : base(context)
    {
    }

    public async Task<AccountInvite?> GetByTokenHashAsync(byte[] tokenHash)
    {
        return await _dbSet
            .Include(i => i.TargetMember)
            .Include(i => i.TargetUser)
            .FirstOrDefaultAsync(i => i.TokenHash.SequenceEqual(tokenHash));
    }

    public async Task<AccountInvite?> GetActiveInviteForMemberAsync(int memberId)
    {
        return await _dbSet
            .Where(i => i.TargetMemberID == memberId && i.UsedAtUtc == null && i.RevokedAtUtc == null)
            .OrderByDescending(i => i.CreatedAtUtc)
            .FirstOrDefaultAsync();
    }

    public async Task<AccountInvite?> GetActiveInviteForUserAsync(int userId)
    {
        return await _dbSet
            .Where(i => i.TargetUserID == userId && i.UsedAtUtc == null && i.RevokedAtUtc == null)
            .OrderByDescending(i => i.CreatedAtUtc)
            .FirstOrDefaultAsync();
    }
}
