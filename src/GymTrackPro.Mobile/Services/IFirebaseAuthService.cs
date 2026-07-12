using System.Threading;
using System.Threading.Tasks;

namespace GymTrackPro.Mobile.Services
{
    public interface IFirebaseAuthService : IAuthenticationSession
    {
        Task<string> LoginAsync(string email, string password);
        Task<string> RegisterAsync(string email, string password);
        Task ResetPasswordAsync(string email);
        Task<string> LoginWithGoogleAsync(string oauthToken);
        Task LogoutAsync();

        Task SendEmailVerificationAsync(CancellationToken cancellationToken = default);
        Task<bool> IsEmailVerifiedAsync(CancellationToken cancellationToken = default);

        // Compatibility members retained until the root session/navigation handoff is wired.
        Task<string?> GetFreshTokenAsync();
        Task<bool> HasValidSessionAsync();
        string? GetCurrentUid();
    }
}
