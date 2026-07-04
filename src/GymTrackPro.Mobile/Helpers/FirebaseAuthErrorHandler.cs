using System;
using Firebase.Auth;

namespace GymTrackPro.Mobile.Helpers
{
    public static class FirebaseAuthErrorHandler
    {
        public static string GetErrorMessage(Exception ex)
        {
            if (ex is FirebaseAuthException authEx)
            {
                switch (authEx.Reason)
                {
                    case AuthErrorReason.WrongPassword:
                        return "Invalid password. Please try again.";
                    case AuthErrorReason.UnknownEmailAddress:
                        return "No account found with this email address.";
                    case AuthErrorReason.EmailExists:
                        return "An account with this email already exists.";
                    case AuthErrorReason.WeakPassword:
                        return "The password provided is too weak.";
                    case AuthErrorReason.UserDisabled:
                        return "This account has been disabled.";
                    case AuthErrorReason.InvalidEmailAddress:
                        return "The email address is badly formatted.";
                    default:
                        return "Authentication failed. Please check your credentials.";
                }
            }
            return "Authentication failed. Please check your credentials.";
        }
    }
}
