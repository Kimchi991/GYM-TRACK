namespace GymTrackPro.API.Services;

public static class ProfilePicturePathPolicy
{
    public const string PrivatePrefix = "profile:";
    public const string LegacyPublicPrefix = "/uploads/profiles/";

    public static bool TryResolveOwnedFile(
        string applicationBaseDirectory,
        string? storedReference,
        out string fullPath) => TryResolveOwnedFile(
            applicationBaseDirectory,
            storedReference,
            out fullPath,
            out _);

    public static bool TryResolveOwnedFile(
        string applicationBaseDirectory,
        string? storedReference,
        out string fullPath,
        out string contentType)
    {
        fullPath = string.Empty;
        contentType = string.Empty;
        if (string.IsNullOrWhiteSpace(storedReference))
        {
            return false;
        }

        string root;
        string fileName;
        if (storedReference.StartsWith(PrivatePrefix, StringComparison.Ordinal))
        {
            fileName = storedReference[PrivatePrefix.Length..];
            root = Path.Combine(applicationBaseDirectory, "App_Data", "profile-pictures");
        }
        else if (storedReference.StartsWith(LegacyPublicPrefix, StringComparison.Ordinal))
        {
            fileName = storedReference[LegacyPublicPrefix.Length..];
            root = Path.Combine(applicationBaseDirectory, "wwwroot", "uploads", "profiles");
        }
        else
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        var isPrivateExtension = extension is ".jpg" or ".png";
        var isLegacyExtension = extension == ".jpg";
        var isLegacy = storedReference.StartsWith(LegacyPublicPrefix, StringComparison.Ordinal);
        if (fileName.Length == 0
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || !(isLegacy ? isLegacyExtension : isPrivateExtension)
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "D", out _))
        {
            return false;
        }

        try
        {
            var canonicalRoot = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(Path.Combine(canonicalRoot, fileName));
            var rootWithSeparator = canonicalRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!candidate.StartsWith(rootWithSeparator, comparison)
                || !string.Equals(
                    Path.GetDirectoryName(candidate),
                    canonicalRoot,
                    comparison))
            {
                return false;
            }

            fullPath = candidate;
            contentType = extension == ".png" ? "image/png" : "image/jpeg";
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
