namespace GymTrackPro.Mobile.Services;

// The production source uses MAUI FileSystem. Tests inject an explicit SQLite
// path; this stub only satisfies the linked parameterless constructor reference.
internal static class FileSystem
{
    public static string AppDataDirectory => Path.GetTempPath();
}
