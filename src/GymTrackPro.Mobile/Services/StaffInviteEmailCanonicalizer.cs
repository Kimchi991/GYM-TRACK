using System.Globalization;
using System.Text;

namespace GymTrackPro.Mobile.Services;

/// <summary>
/// Mirrors the API identity-store email canonicalization contract. Keep this in
/// lockstep with EmailNormalization.TryCanonicalize on the server.
/// </summary>
internal static class StaffInviteEmailCanonicalizer
{
    public static bool TryCanonicalize(
        string? value,
        out string canonicalEmail,
        out string normalizedEmail)
    {
        canonicalEmail = string.Empty;
        normalizedEmail = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 255
            || trimmed.Any(char.IsControl)
            || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        try
        {
            canonicalEmail = trimmed.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            canonicalEmail = string.Empty;
            return false;
        }
        if (canonicalEmail.Length is 0 or > 255
            || canonicalEmail.Any(char.IsControl)
            || canonicalEmail.Any(char.IsWhiteSpace))
        {
            canonicalEmail = string.Empty;
            return false;
        }

        normalizedEmail = canonicalEmail.ToUpper(CultureInfo.InvariantCulture);
        if (normalizedEmail.Length is 0 or > 255)
        {
            canonicalEmail = string.Empty;
            normalizedEmail = string.Empty;
            return false;
        }

        return true;
    }
}
