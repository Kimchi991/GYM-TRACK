namespace GymTrackPro.API.Services;

public static class ProfilePicturePathPolicy
{
    private const string PublicPrefix = "/uploads/profiles/";

    public static bool TryResolveOwnedFile(
        string applicationBaseDirectory,
        string? storedPath,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(storedPath)
            || !storedPath.StartsWith(PublicPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = storedPath[PublicPrefix.Length..];
        if (fileName.Length == 0
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || !string.Equals(Path.GetExtension(fileName), ".jpg", StringComparison.Ordinal)
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "D", out _))
        {
            return false;
        }

        try
        {
            var root = Path.GetFullPath(Path.Combine(
                applicationBaseDirectory,
                "wwwroot",
                "uploads",
                "profiles"));
            var candidate = Path.GetFullPath(Path.Combine(root, fileName));
            var rootWithSeparator = root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!candidate.StartsWith(rootWithSeparator, comparison)
                || !string.Equals(
                    Path.GetDirectoryName(candidate),
                    root,
                    comparison))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }
}
