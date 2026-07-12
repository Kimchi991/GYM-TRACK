namespace GymTrackPro.API.Services;

public sealed record ProfilePictureContent(Stream Stream, string ContentType);

public interface IProfilePictureStorage
{
    string Store(string base64Data, int configuredMaximumBytes);
    ProfilePictureContent? OpenRead(string? storedReference);
    bool TryDelete(string? storedReference);
}

public sealed class FileSystemProfilePictureStorage : IProfilePictureStorage
{
    public const int AbsoluteMaximumBytes = 10 * 1024 * 1024;
    private readonly string _applicationBaseDirectory;

    public FileSystemProfilePictureStorage()
        : this(AppContext.BaseDirectory)
    {
    }

    public FileSystemProfilePictureStorage(string applicationBaseDirectory)
    {
        _applicationBaseDirectory = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? throw new ArgumentException(
                "An application base directory is required.",
                nameof(applicationBaseDirectory))
            : Path.GetFullPath(applicationBaseDirectory);
    }

    public string Store(string base64Data, int configuredMaximumBytes)
    {
        if (string.IsNullOrWhiteSpace(base64Data))
        {
            throw new ArgumentException("A profile picture is required.", nameof(base64Data));
        }

        var maximumBytes = Math.Clamp(configuredMaximumBytes, 1, AbsoluteMaximumBytes);
        var separatorIndex = base64Data.IndexOf(',');
        var encoded = separatorIndex >= 0
            ? base64Data[(separatorIndex + 1)..]
            : base64Data;
        var maximumEncodedLength = checked(((maximumBytes + 2L) / 3L) * 4L);
        if (encoded.Length > maximumEncodedLength)
        {
            throw new ArgumentException(
                $"Profile picture size must be between 1 and {maximumBytes} bytes.",
                nameof(base64Data));
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "Invalid profile picture Base64 format.",
                nameof(base64Data),
                exception);
        }

        if (bytes.Length == 0 || bytes.Length > maximumBytes)
        {
            throw new ArgumentException(
                $"Profile picture size must be between 1 and {maximumBytes} bytes.",
                nameof(base64Data));
        }

        var extension = DetectExtension(bytes)
            ?? throw new ArgumentException(
                "Profile pictures must be valid JPEG or PNG files.",
                nameof(base64Data));
        var fileName = $"{Guid.NewGuid():D}{extension}";
        var storedReference = ProfilePicturePathPolicy.PrivatePrefix + fileName;
        if (!ProfilePicturePathPolicy.TryResolveOwnedFile(
                _applicationBaseDirectory,
                storedReference,
                out var fullPath))
        {
            throw new InvalidOperationException("The profile picture storage path is invalid.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using (var stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            options: FileOptions.WriteThrough))
        {
            stream.Write(bytes);
        }

        return storedReference;
    }

    public ProfilePictureContent? OpenRead(string? storedReference)
    {
        if (!ProfilePicturePathPolicy.TryResolveOwnedFile(
                _applicationBaseDirectory,
                storedReference,
                out var fullPath,
                out var contentType)
            || !File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 81920,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                Span<byte> signature = stackalloc byte[8];
                var bytesRead = stream.Read(signature);
                var detectedContentType = DetectContentType(signature[..bytesRead]);
                var isLegacyReference = storedReference!.StartsWith(
                    ProfilePicturePathPolicy.LegacyPublicPrefix,
                    StringComparison.Ordinal);
                if (detectedContentType is null
                    || (!isLegacyReference
                        && !string.Equals(
                            detectedContentType,
                            contentType,
                            StringComparison.Ordinal)))
                {
                    stream.Dispose();
                    return null;
                }

                stream.Position = 0;
                return new ProfilePictureContent(stream, detectedContentType);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool TryDelete(string? storedReference)
    {
        if (!ProfilePicturePathPolicy.TryResolveOwnedFile(
                _applicationBaseDirectory,
                storedReference,
                out var fullPath))
        {
            return false;
        }

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? DetectExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        ReadOnlySpan<byte> pngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return bytes.StartsWith(pngSignature) ? ".png" : null;
    }

    private static string? DetectContentType(ReadOnlySpan<byte> bytes) =>
        DetectExtension(bytes) switch
        {
            ".jpg" => "image/jpeg",
            ".png" => "image/png",
            _ => null
        };
}
