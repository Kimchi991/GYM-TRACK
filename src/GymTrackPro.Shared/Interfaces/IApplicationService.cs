using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IApplicationService
{
    Task<ApplicationListItemDto> SubmitApplicationAsync(SubmitApplicationDto dto);
    Task<IEnumerable<ApplicationListItemDto>> GetPendingApplicationsAsync();
    Task<ApplicationListItemDto> VerifyApplicationAsync(int id, int actorUserId, VerifyApplicationDto dto);
}
