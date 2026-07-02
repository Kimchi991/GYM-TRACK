using GymTrackPro.API.Data;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.API.Repositories;

public class WalkInVisitorRepository : BaseRepository<WalkInVisitor>, IWalkInVisitorRepository
{
    public WalkInVisitorRepository(GymDbContext context) : base(context)
    {
    }
}
