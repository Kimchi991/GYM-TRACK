using System.Threading.Tasks;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Shared.Interfaces;

public interface IMembershipPlanRepository : IBaseRepository<MembershipPlan>
{
    Task<MembershipPlan?> GetByNameAsync(string name);
}
