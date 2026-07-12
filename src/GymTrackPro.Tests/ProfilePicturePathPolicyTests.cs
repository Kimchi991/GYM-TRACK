using GymTrackPro.API.Services;

namespace GymTrackPro.Tests;

public sealed class ProfilePicturePathPolicyTests
{
    private const string FileName = "c56a4180-65aa-42ec-a945-5fd21dec0538.jpg";

    [Fact]
    public void Generated_profile_path_resolves_under_exact_owned_root()
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
    [InlineData("/uploads/profiles/../../appsettings.json")]
    [InlineData("/uploads/profiles/subdir/c56a4180-65aa-42ec-a945-5fd21dec0538.jpg")]
    [InlineData("/uploads/profiles\\c56a4180-65aa-42ec-a945-5fd21dec0538.jpg")]
    [InlineData("/uploads/profiles/c56a4180-65aa-42ec-a945-5fd21dec0538.png")]
    [InlineData("/uploads/profiles/not-a-guid.jpg")]
    [InlineData("/uploads/profiles/c56a418065aa42eca9455fd21dec0538.jpg")]
    [InlineData("/uploads/profiles/c56a4180-65aa-42ec-a945-5fd21dec0538.JPG")]
    [InlineData("/uploads/profiles-archive/c56a4180-65aa-42ec-a945-5fd21dec0538.jpg")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    public void Unowned_or_unexpected_path_is_refused(string storedPath)
    {
        var accepted = ProfilePicturePathPolicy.TryResolveOwnedFile(
            Path.Combine(Path.GetTempPath(), "gymtrackpro-policy"),
            storedPath,
            out var fullPath);

        Assert.False(accepted);
        Assert.Equal(string.Empty, fullPath);
    }
}
