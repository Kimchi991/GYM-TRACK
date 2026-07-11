using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IAuthenticationService
{
    Task<UserResponseDto> SyncUserAsync(string firebaseUid, string email);
    Task<UserResponseDto?> AuthenticateAsync(LoginDto loginDto);
    Task<UserResponseDto> RegisterAsync(RegisterUserDto registerUserDto);
    Task<bool> VerifyEmailAsync(string email, string token);
    Task<bool> ForgotPasswordAsync(string email);
    Task<bool> ResetPasswordAsync(ResetPasswordDto resetPasswordDto);
}
