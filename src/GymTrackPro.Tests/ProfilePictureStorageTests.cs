using GymTrackPro.API.Services;

namespace GymTrackPro.Tests;

public sealed class ProfilePictureStorageTests : IDisposable
{
    private readonly string _baseDirectory = Path.Combine(
        Path.GetTempPath(),
        "gymtrackpro-profile-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Jpeg_is_stored_privately_and_can_be_opened_and_deleted()
    {
        var storage = new FileSystemProfilePictureStorage(_baseDirectory);
        var encoded = Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0]);

        var reference = storage.Store(encoded, 1024);

        Assert.StartsWith(ProfilePicturePathPolicy.PrivatePrefix, reference, StringComparison.Ordinal);
        Assert.DoesNotContain("wwwroot", reference, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            ':',
            Path.GetFileName(
                AssertPath(reference)));
        var content = storage.OpenRead(reference);
        Assert.NotNull(content);
        Assert.Equal("image/jpeg", content.ContentType);
        using var stream = content.Stream;
        Assert.True(storage.TryDelete(reference));
        Assert.Null(storage.OpenRead(reference));
    }

    [Fact]
    public void Png_signature_is_detected_even_when_data_uri_metadata_is_untrusted()
    {
        var storage = new FileSystemProfilePictureStorage(_baseDirectory);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var reference = storage.Store(
            $"data:text/plain;base64,{Convert.ToBase64String(png)}",
            1024);

        Assert.EndsWith(".png", reference, StringComparison.Ordinal);
        var content = storage.OpenRead(reference);
        Assert.NotNull(content);
        Assert.Equal("image/png", content.ContentType);
        using var stream = content.Stream;
    }

    [Theory]
    [InlineData("bm90LWFuLWltYWdl")]
    [InlineData("%%%")]
    [InlineData("")]
    public void Invalid_or_empty_payload_is_rejected(string encoded)
    {
        var storage = new FileSystemProfilePictureStorage(_baseDirectory);

        Assert.Throws<ArgumentException>(() => storage.Store(encoded, 1024));
    }

    [Fact]
    public void Configured_size_limit_is_enforced()
    {
        var storage = new FileSystemProfilePictureStorage(_baseDirectory);
        var encoded = Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0, 0x00]);

        Assert.Throws<ArgumentException>(() => storage.Store(encoded, 4));
    }

    [Fact]
    public void Legacy_png_bytes_saved_with_jpg_name_are_served_as_png_and_rewound()
    {
        var storage = new FileSystemProfilePictureStorage(_baseDirectory);
        const string legacyReference =
            "/uploads/profiles/c56a4180-65aa-42ec-a945-5fd21dec0538.jpg";
        Assert.True(ProfilePicturePathPolicy.TryResolveOwnedFile(
            _baseDirectory,
            legacyReference,
            out var legacyPath));
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01 };
        File.WriteAllBytes(legacyPath, png);

        var content = storage.OpenRead(legacyReference);

        Assert.NotNull(content);
        Assert.Equal("image/png", content.ContentType);
        Assert.Equal(0, content.Stream.Position);
        Assert.Equal(0x89, content.Stream.ReadByte());
        content.Stream.Dispose();
    }

    [Fact]
    public void Legacy_file_with_unknown_signature_is_rejected()
    {
        var storage = new FileSystemProfilePictureStorage(_baseDirectory);
        const string legacyReference =
            "/uploads/profiles/c56a4180-65aa-42ec-a945-5fd21dec0538.jpg";
        Assert.True(ProfilePicturePathPolicy.TryResolveOwnedFile(
            _baseDirectory,
            legacyReference,
            out var legacyPath));
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllBytes(legacyPath, [0x01, 0x02, 0x03, 0x04]);

        Assert.Null(storage.OpenRead(legacyReference));
    }

    [Fact]
    public void Oversized_base64_is_rejected_before_decode()
    {
        var storage = new FileSystemProfilePictureStorage(_baseDirectory);
        var oversized = new string('A', 4096);

        Assert.Throws<ArgumentException>(() => storage.Store(oversized, 4));
        Assert.False(Directory.Exists(Path.Combine(
            _baseDirectory,
            "App_Data",
            "profile-pictures")));
    }

    private string AssertPath(string storedReference)
    {
        Assert.True(ProfilePicturePathPolicy.TryResolveOwnedFile(
            _baseDirectory,
            storedReference,
            out var fullPath));
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }
}
