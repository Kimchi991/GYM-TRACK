using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IAuthenticationService
{
    Task<UserResponseDto?> AuthenticateAsync(LoginDto loginDto);
    Task<UserResponseDto> RegisterAsync(RegisterUserDto registerUserDto);
    Task<bool> ForgotPasswordAsync(string email);
    Task<bool> ResetPasswordAsync(ResetPasswordDto resetPasswordDto);
    Task<bool> VerifyEmailAsync(string email, string token);
}
