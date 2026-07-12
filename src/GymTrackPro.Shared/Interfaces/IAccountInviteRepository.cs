using System.Threading.Tasks;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Shared.Interfaces;

public interface IAccountInviteRepository : IBaseRepository<AccountInvite>
{
    Task<AccountInvite?> GetByTokenHashAsync(byte[] tokenHash);
    Task<AccountInvite?> GetActiveInviteForMemberAsync(int memberId);
    Task<AccountInvite?> GetActiveInviteForUserAsync(int userId);
}
