using System;
using System.Threading.Tasks;
using Firebase.Auth;

namespace GymTrackPro.Mobile.Services
{
    public class FirebaseAuthService : IFirebaseAuthService
    {
        private readonly FirebaseAuthProvider _provider;

        public FirebaseAuthService()
        {
            _provider = new FirebaseAuthProvider(new FirebaseConfig("AIzaSyBMzATrUqFPp2xgY2gsVqaqsqeua0vEoAk"));
        }

        public async Task<string> LoginAsync(string email, string password)
        {
            var authLink = await _provider.SignInWithEmailAndPasswordAsync(email, password);
            return authLink.FirebaseToken;
        }

        public async Task<string> RegisterAsync(string email, string password)
        {
            var authLink = await _provider.CreateUserWithEmailAndPasswordAsync(email, password);
            return authLink.FirebaseToken;
        }

        public async Task ResetPasswordAsync(string email)
        {
            await _provider.SendPasswordResetEmailAsync(email);
        }

        public async Task<string> LoginWithGoogleAsync(string oauthToken)
        {
            var authLink = await _provider.SignInWithOAuthAsync(FirebaseAuthType.Google, oauthToken);
            return authLink.FirebaseToken;
        }

        public Task LogoutAsync()
        {
            // FirebaseAuthentication.net is stateless on the client side.
            // Clear any stored token so the app treats the user as signed out.
            SecureStorage.Default.Remove("firebase_token");
            return Task.CompletedTask;
        }
    }
}
