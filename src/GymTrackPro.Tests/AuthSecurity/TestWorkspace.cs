using System.Runtime.CompilerServices;

namespace GymTrackPro.Tests.AuthSecurity;

internal static class TestWorkspace
{
    public static string FindRoot([CallerFilePath] string sourceFilePath = "")
    {
        var sourceDirectory = Path.GetDirectoryName(sourceFilePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new DirectoryNotFoundException("Test source directory could not be located.");
        }

        var candidate = new DirectoryInfo(sourceDirectory);
        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "src", "GymTrackPro.slnx")) &&
                Directory.Exists(Path.Combine(candidate.FullName, "src", "GymTrackPro.API")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw new DirectoryNotFoundException("Workspace root could not be located from the test source path.");
    }
}
