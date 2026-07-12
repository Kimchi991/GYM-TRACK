using System.Threading;
using System.Threading.Tasks;

namespace GymTrackPro.Mobile.Services
{
    /// <summary>
    /// Signals the recoverable state where Firebase created and persisted the account,
    /// but the separate verification-email request did not complete.
    /// </summary>
    public sealed class FirebaseRegistrationVerificationPendingException : Exception
    {
        public FirebaseRegistrationVerificationPendingException(Exception innerException)
            : base(
                "The account was created, but verification email delivery did not complete.",
                innerException)
        {
        }
    }

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
