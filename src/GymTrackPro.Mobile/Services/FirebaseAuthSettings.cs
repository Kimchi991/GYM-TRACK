namespace GymTrackPro.Mobile.Services;

internal static class FirebaseAuthSettings
{
    private const string ApplicationNamespace =
        "com.companyname.gymtrackpro.mobile.auth.firebase";
    private const string StorageSchemaVersion = "v1";

    internal const string ApiKey = "AIzaSyBMzATrUqFPp2xgY2gsVqaqsqeua0vEoAk";
    internal const string AuthDomain = "fithub-cf45f.firebaseapp.com";
    internal const string ProjectId = "fithub-cf45f";

#if DEBUG
    internal const string EnvironmentName = "development";
#else
    internal const string EnvironmentName = "production";
#endif

    // App-owned and versioned so another app, Firebase project, or environment cannot
    // accidentally restore this session. Keep this synchronized with ApplicationId.
    internal static readonly string UserStorageKey = BuildUserStorageKey(EnvironmentName);

    internal static readonly string[] LegacyStorageKeys =
    [
        "auth_token",
        "firebase_token",
        "firebase_user_v1"
    ];

    internal static string BuildUserStorageKey(string environmentName)
    {
        if (environmentName is not ("development" or "production"))
        {
            throw new ArgumentException(
                "Firebase session environment must be 'development' or 'production'.",
                nameof(environmentName));
        }

        return $"{ApplicationNamespace}.{ProjectId}.{environmentName}.{StorageSchemaVersion}.user";
    }
}
