using Foundation;
using Microsoft.Maui.Storage;

namespace GymTrackPro.Mobile;

internal static class AppleBackupExclusion
{
    internal static bool Apply(out string? errorMessage)
    {
        var failures = new List<string>();
        Exclude("application data", FileSystem.AppDataDirectory, failures);
        Exclude("cache", FileSystem.CacheDirectory, failures);

        errorMessage = failures.Count == 0 ? null : string.Join("; ", failures);
        return failures.Count == 0;
    }

    private static void Exclude(string label, string path, ICollection<string> failures)
    {
        try
        {
            Directory.CreateDirectory(path);
            var error = NSFileManager.SetSkipBackupAttribute(path, true);
            if (error is not null)
            {
                failures.Add($"{label}: {error.Domain}/{error.Code}: {error.LocalizedDescription}");
            }
        }
        catch (Exception exception)
        {
            failures.Add($"{label}: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
