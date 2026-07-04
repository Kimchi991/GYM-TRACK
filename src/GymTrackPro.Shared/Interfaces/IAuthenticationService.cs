using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IAuthenticationService
{
    Task<UserResponseDto> SyncUserAsync(string firebaseUid, string email);
}
