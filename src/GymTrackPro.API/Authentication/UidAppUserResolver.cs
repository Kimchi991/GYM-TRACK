using System.Globalization;
using System.Text;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymTrackPro.API.Authentication;

public enum AppUserResolutionStatus
{
    Resolved,
    InvalidIdentity,
    NotFound,
    Ambiguous,
    Inactive,
    InvalidLink,
    UnsupportedRole
}

public sealed record AppUserIdentity(
    int UserId,
    string FirebaseUid,
    int? MemberId,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    UserRole Role,
    bool IsActive,
    bool EmailVerified,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastLoginAt)
{
    public UserResponseDto ToResponse() => new()
    {
        UserID = UserId,
        MemberID = MemberId,
        Username = Username,
        Email = Email,
        FirstName = FirstName,
        LastName = LastName,
        Role = Role,
        IsActive = IsActive,
        EmailVerified = EmailVerified,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        LastLoginAt = LastLoginAt,
        OnboardingState = Role == UserRole.GymGoer && !MemberId.HasValue
            ? "ACCOUNT_PENDING_ACTIVATION"
            : "COMPLETED",
        Capabilities = Role switch
        {
            UserRole.Administrator =>
                new[] { Policies.ActiveAppUser, Policies.BackOffice, Policies.OwnerOnly },
            UserRole.Receptionist =>
                new[] { Policies.ActiveAppUser, Policies.BackOffice },
            UserRole.GymGoer when MemberId.HasValue =>
                new[] { Policies.ActiveAppUser, Policies.GymGoerSelf },
            _ => Array.Empty<string>()
        }
    };
}

public sealed record AppUserResolution(
    AppUserResolutionStatus Status,
    AppUserIdentity? User = null);

public interface IUidAppUserResolver
{
    Task<AppUserResolution> ResolveAsync(
        string firebaseUid,
        string verifiedEmail,
        CancellationToken cancellationToken = default);
}

public class UidAppUserResolver : IUidAppUserResolver
{
    private readonly GymDbContext _dbContext;
    private readonly ILogger<UidAppUserResolver> _logger;
    private bool _hasCachedResolution;
    private string? _cachedUid;
    private string? _cachedEmail;
    private AppUserResolution? _cachedResolution;

    public UidAppUserResolver(
        GymDbContext dbContext,
        ILogger<UidAppUserResolver> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AppUserResolution> ResolveAsync(
        string firebaseUid,
        string verifiedEmail,
        CancellationToken cancellationToken = default)
    {
        if (!FirebaseIdentityValidation.TryValidateUid(firebaseUid)
            || !EmailNormalization.TryNormalize(verifiedEmail, out var normalizedEmail))
        {
            return new AppUserResolution(AppUserResolutionStatus.InvalidIdentity);
        }

        if (_hasCachedResolution
            && string.Equals(_cachedUid, firebaseUid, StringComparison.Ordinal)
            && string.Equals(_cachedEmail, normalizedEmail, StringComparison.Ordinal))
        {
            return _cachedResolution!;
        }

        // The database comparison is only a coarse candidate query because a legacy SQL
        // collation may be case-insensitive. Firebase UIDs are accepted only by exact ordinal
        // equality in memory, and a duplicate exact match fails closed.
        var coarseCandidates = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.FirebaseUid == firebaseUid)
            .Select(user => new UidResolutionCandidate(
                new AppUserIdentity(
                    user.UserID,
                    user.FirebaseUid!,
                    user.MemberID,
                    user.Username,
                    user.Email,
                    user.FirstName,
                    user.LastName,
                    user.Role,
                    user.IsActive,
                    user.EmailVerified,
                    user.CreatedAt,
                    user.UpdatedAt,
                    user.LastLoginAt),
                !user.MemberID.HasValue || _dbContext.Members.Any(
                    member => member.MemberID == user.MemberID.Value),
                user.MemberID.HasValue && _dbContext.Members.Any(
                    member => member.MemberID == user.MemberID.Value && member.IsDeleted)))
            .Take(3)
            .ToListAsync(cancellationToken);

        var candidates = coarseCandidates
            .Where(candidate => string.Equals(
                candidate.Identity.FirebaseUid,
                firebaseUid,
                StringComparison.Ordinal))
            .Take(2)
            .ToList();

        AppUserResolution resolution;
        if (candidates.Count == 0)
        {
            resolution = new AppUserResolution(AppUserResolutionStatus.NotFound);
        }
        else if (candidates.Count > 1)
        {
            _logger.LogWarning("UID identity lookup was denied because multiple SQL users matched exactly.");
            resolution = new AppUserResolution(AppUserResolutionStatus.Ambiguous);
        }
        else if (!candidates[0].Identity.IsActive)
        {
            resolution = new AppUserResolution(AppUserResolutionStatus.Inactive);
        }
        else if (!Enum.IsDefined(candidates[0].Identity.Role))
        {
            resolution = new AppUserResolution(AppUserResolutionStatus.UnsupportedRole);
        }
        else if (candidates[0].Identity.Role == UserRole.GymGoer
            && candidates[0].MemberIsDeleted)
        {
            resolution = new AppUserResolution(AppUserResolutionStatus.Inactive);
        }
        else if ((candidates[0].Identity.Role == UserRole.GymGoer
                && (!candidates[0].Identity.MemberId.HasValue || !candidates[0].MemberExists))
            || (candidates[0].Identity.Role is UserRole.Administrator or UserRole.Receptionist
                && candidates[0].Identity.MemberId.HasValue))
        {
            _logger.LogWarning("UID identity lookup was denied because the SQL role/member link is inconsistent.");
            resolution = new AppUserResolution(AppUserResolutionStatus.InvalidLink);
        }
        else
        {
            resolution = new AppUserResolution(
                AppUserResolutionStatus.Resolved,
                candidates[0].Identity);
        }

        _hasCachedResolution = true;
        _cachedUid = firebaseUid;
        _cachedEmail = normalizedEmail;
        _cachedResolution = resolution;
        return resolution;
    }

    private sealed record UidResolutionCandidate(
        AppUserIdentity Identity,
        bool MemberExists,
        bool MemberIsDeleted);
}

public static class FirebaseIdentityValidation
{
    public static bool TryValidateUid(string? firebaseUid) =>
        !string.IsNullOrWhiteSpace(firebaseUid)
        && firebaseUid.Length <= 128
        && !firebaseUid.Any(char.IsControl)
        && !firebaseUid.Any(char.IsWhiteSpace);
}

public static class EmailNormalization
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

        canonicalEmail = trimmed.Normalize(NormalizationForm.FormKC);
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

    public static bool TryNormalize(string? value, out string normalizedEmail)
    {
        var valid = TryCanonicalize(value, out _, out normalizedEmail);
        return valid;
    }
}
