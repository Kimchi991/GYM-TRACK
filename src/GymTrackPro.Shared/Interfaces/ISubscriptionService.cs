using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface ISubscriptionService
{
    Task<SubscriptionResponseDto?> GetByIdAsync(int id);
    Task<IEnumerable<SubscriptionResponseDto>> GetByMemberIdAsync(int memberId);
    Task<SubscriptionResponseDto> SubscribeMemberAsync(CreateSubscriptionDto subscribeDto);
    Task PauseSubscriptionAsync(int subscriptionID, string reason);
    Task ResumeSubscriptionAsync(int subscriptionID);
}
