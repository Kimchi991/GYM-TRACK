using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IAuthenticationService
{
    Task<UserResponseDto> SyncUserAsync(string firebaseUid, string email);
    Task<UserResponseDto> GetCurrentUserAsync(int userId, string firebaseUid);
    Task<UserResponseDto> ActivateAppAsync(string firebaseUid, string email, ActivateInviteDto request);
    Task<AppInviteCodeResponseDto> CreateMemberInviteAsync(int memberId, int creatorUserId, CreateAppInviteDto dto);
    Task<AppInviteResponseDto> GetMemberInviteStatusAsync(int memberId);
    Task RevokeMemberInviteAsync(int memberId, int actorUserId);
    Task<AppInviteCodeResponseDto> CreateUserInviteAsync(int userId, int creatorUserId, CreateAppInviteDto dto);
    Task<AppInviteResponseDto> GetUserInviteStatusAsync(int userId);
    Task RevokeUserInviteAsync(int userId, int actorUserId);
}
