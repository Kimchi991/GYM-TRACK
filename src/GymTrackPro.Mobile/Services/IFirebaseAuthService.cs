using System.Threading.Tasks;

namespace GymTrackPro.Mobile.Services
{
    public interface IFirebaseAuthService
    {
        Task<string> LoginAsync(string email, string password);
        Task<string> RegisterAsync(string email, string password);
        Task ResetPasswordAsync(string email);
        Task<string> LoginWithGoogleAsync(string oauthToken);
        Task LogoutAsync();
    }
}
