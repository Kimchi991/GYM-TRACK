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
                    case AuthErrorReason.OperationNotAllowed:
                        return "Sign-in is not enabled for this account type.";
                    default:
                        // Catch the "conflicts with existing account state" error
                        // which Firebase returns as a raw message rather than an enum.
                        if (ex.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
                            ex.Message.Contains("existing account state", StringComparison.OrdinalIgnoreCase))
                        {
                            return "A previous session is still active. Please try again.";
                        }
                        return "Authentication failed. Please check your credentials.";
                }
            }
            return "Authentication failed. Please check your credentials.";
        }
    }
}
