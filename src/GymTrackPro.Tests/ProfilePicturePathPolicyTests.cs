using GymTrackPro.API.Services;

namespace GymTrackPro.Tests;

public sealed class ProfilePicturePathPolicyTests
{
    private const string FileName = "c56a4180-65aa-42ec-a945-5fd21dec0538.jpg";

    [Fact]
    public void Private_reference_resolves_under_non_public_owned_root()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "gymtrackpro-policy");

        var accepted = ProfilePicturePathPolicy.TryResolveOwnedFile(
            baseDirectory,
            $"profile:{FileName}",
            out var fullPath,
            out var contentType);

        Assert.True(accepted);
        Assert.Equal("image/jpeg", contentType);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                baseDirectory,
                "App_Data",
                "profile-pictures",
                FileName)),
            fullPath);
    }

    [Fact]
    public void Exact_legacy_reference_is_readable_only_from_legacy_owned_root()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "gymtrackpro-policy");

        var accepted = ProfilePicturePathPolicy.TryResolveOwnedFile(
            baseDirectory,
            $"/uploads/profiles/{FileName}",
            out var fullPath);

        Assert.True(accepted);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                baseDirectory,
                "wwwroot",
                "uploads",
                "profiles",
                FileName)),
            fullPath);
    }

    [Theory]
    [InlineData("profile:../../appsettings.json")]
    [InlineData("profile:subdir/c56a4180-65aa-42ec-a945-5fd21dec0538.jpg")]
    [InlineData("profile:c56a4180-65aa-42ec-a945-5fd21dec0538.gif")]
    [InlineData("profile:not-a-guid.jpg")]
    [InlineData("profile:c56a4180-65aa-42ec-a945-5fd21dec0538.JPG")]
    [InlineData("/uploads/profiles/../../appsettings.json")]
    [InlineData("/uploads/profiles/c56a4180-65aa-42ec-a945-5fd21dec0538.png")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    public void Unowned_or_unexpected_reference_is_refused(string storedReference)
    {
        var accepted = ProfilePicturePathPolicy.TryResolveOwnedFile(
            Path.Combine(Path.GetTempPath(), "gymtrackpro-policy"),
            storedReference,
            out var fullPath);

        Assert.False(accepted);
        Assert.Equal(string.Empty, fullPath);
    }
}
