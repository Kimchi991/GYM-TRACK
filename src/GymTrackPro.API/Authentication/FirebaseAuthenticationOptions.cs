using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace GymTrackPro.API.Authentication;

public sealed partial class FirebaseAuthenticationOptions
{
    public const string SectionName = "FirebaseAuthentication";

    public string ProjectId { get; set; } = string.Empty;

    public string Issuer => $"https://securetoken.google.com/{ProjectId}";

    public bool HasValidProjectId => ProjectIdPattern().IsMatch(ProjectId)
        && !ProjectId.Contains("google", StringComparison.Ordinal)
        && !ProjectId.Contains("ssl", StringComparison.Ordinal)
        && !ProjectId.Contains("undefined", StringComparison.Ordinal)
        && !ProjectId.Contains("null", StringComparison.Ordinal);

    [GeneratedRegex("^[a-z][a-z0-9-]{4,28}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectIdPattern();
}

public sealed class FirebaseAuthenticationOptionsValidator : IValidateOptions<FirebaseAuthenticationOptions>
{
    private readonly IHostEnvironment _environment;

    public FirebaseAuthenticationOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, FirebaseAuthenticationOptions options)
    {
        if (options.HasValidProjectId)
        {
            return ValidateOptionsResult.Success;
        }

        if (_environment.IsDevelopment() || _environment.IsEnvironment("Testing"))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{FirebaseAuthenticationOptions.SectionName}:ProjectId must contain the exact Firebase project ID.");
    }
}
