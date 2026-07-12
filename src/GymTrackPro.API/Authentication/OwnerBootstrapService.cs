using System.Data;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymTrackPro.API.Authentication;

public sealed class OwnerBootstrapOptions
{
    public const string SectionName = "OwnerBootstrap";

    public bool Enabled { get; set; }
    public string AllowedEnvironment { get; set; } = string.Empty;
}

public sealed record OwnerBootstrapRequest(
    int UserId,
    string FirebaseUid,
    string ExpectedNormalizedEmail,
    bool DryRun,
    bool Confirm);

public sealed record OwnerBootstrapResult(
    int UserId,
    UserRole Role,
    bool WouldBind,
    bool Applied);

public interface IOwnerBootstrapService
{
    Task<OwnerBootstrapResult> ExecuteAsync(
        OwnerBootstrapRequest request,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default);
}

// This is deliberately a service core, not an HTTP endpoint. The lead-owned CLI must bind
// OwnerBootstrapOptions, register this service, and require explicit noninteractive arguments.
public sealed class OwnerBootstrapService : IOwnerBootstrapService
{
    private const string BootstrapAuditAction = "InitialOwnerFirebaseBound";

    private readonly GymDbContext _dbContext;
    private readonly IClockService _clock;
    private readonly OwnerBootstrapOptions _options;
    private readonly string _environmentName;

    public OwnerBootstrapService(
        GymDbContext dbContext,
        IClockService clock,
        IOptions<OwnerBootstrapOptions> options,
        IHostEnvironment environment)
    {
        _dbContext = dbContext;
        _clock = clock;
        _options = options.Value;
        _environmentName = environment.EnvironmentName;
    }

    public async Task<OwnerBootstrapResult> ExecuteAsync(
        OwnerBootstrapRequest request,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        if (!_options.Enabled
            || string.IsNullOrWhiteSpace(_options.AllowedEnvironment)
            || !string.Equals(
                _options.AllowedEnvironment,
                _environmentName,
                StringComparison.Ordinal))
        {
            throw AccessForbidden();
        }

        try
        {
            var strategy = _dbContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = _dbContext.Database.IsRelational()
                    ? await _dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken)
                    : null;

                try
                {
                    var user = await GetTargetForUpdateAsync(request.UserId, cancellationToken)
                        ?? throw IdentityConflict();

                    // A transient commit acknowledgement failure can cause the execution
                    // strategy to re-enter this delegate after the transaction committed.
                    // Accept only the exact binding written by this operation marker; a
                    // separate CLI invocation still fails closed because its correlation
                    // marker differs.
                    if (await IsExactCommittedBindingAsync(
                            user,
                            request,
                            operationContext,
                            cancellationToken))
                    {
                        return new OwnerBootstrapResult(
                            user.UserID,
                            user.Role,
                            WouldBind: true,
                            Applied: true);
                    }

                    await ValidateTargetAndConflictsAsync(user, request, cancellationToken);

                    if (request.DryRun)
                    {
                        if (transaction is not null)
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                        }

                        _dbContext.ChangeTracker.Clear();
                        return new OwnerBootstrapResult(
                            user.UserID,
                            user.Role,
                            WouldBind: true,
                            Applied: false);
                    }

                    user.FirebaseUid = request.FirebaseUid;
                    user.NormalizedEmail = request.ExpectedNormalizedEmail;
                    user.EmailVerified = true;
                    user.UpdatedAt = _clock.UtcNow;
                    _dbContext.AuditLogs.Add(new AuditLog
                    {
                        UserID = user.UserID,
                        Action = BootstrapAuditAction,
                        Details = BootstrapAuditDetails(operationContext),
                        Timestamp = _clock.UtcNow,
                        IPAddress = SafeIpAddress(operationContext.IpAddress)
                    });

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(cancellationToken);
                    }

                    return new OwnerBootstrapResult(
                        user.UserID,
                        user.Role,
                        WouldBind: true,
                        Applied: true);
                }
                catch
                {
                    if (transaction is not null)
                    {
                        await TryRollbackWithoutMaskingAsync(transaction);
                    }

                    _dbContext.ChangeTracker.Clear();
                    throw;
                }
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            throw IdentityConflict();
        }
        catch (DbUpdateException exception)
            when (IdentityDatabaseConflictClassifier.IsUniqueViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            throw IdentityConflict();
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<bool> IsExactCommittedBindingAsync(
        User user,
        OwnerBootstrapRequest request,
        IdentityOperationContext operationContext,
        CancellationToken cancellationToken)
    {
        if (!user.IsActive
            || user.Role != UserRole.Administrator
            || user.MemberID.HasValue
            || !user.EmailVerified
            || !string.Equals(user.FirebaseUid, request.FirebaseUid, StringComparison.Ordinal)
            || !string.Equals(
                user.NormalizedEmail,
                request.ExpectedNormalizedEmail,
                StringComparison.Ordinal)
            || !EmailNormalization.TryNormalize(user.Email, out var targetEmail)
            || !string.Equals(
                targetEmail,
                request.ExpectedNormalizedEmail,
                StringComparison.Ordinal))
        {
            return false;
        }

        var uidCandidates = await LockUsersByUidAsync(request.FirebaseUid, cancellationToken);
        if (uidCandidates.Count != 1 || uidCandidates[0].UserID != user.UserID)
        {
            return false;
        }

        var boundOwners = await (IsSqlServer
            ? _dbContext.Users.FromSqlRaw(
                "SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [Role] = 0 AND [FirebaseUid] IS NOT NULL")
            : _dbContext.Users.Where(candidate =>
                candidate.Role == UserRole.Administrator
                && candidate.FirebaseUid != null))
            .ToListAsync(cancellationToken);
        if (boundOwners.Count != 1 || boundOwners[0].UserID != user.UserID)
        {
            return false;
        }

        var exactEmailOwners = await (IsSqlServer
            ? _dbContext.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [NormalizedEmail] = {request.ExpectedNormalizedEmail}")
            : _dbContext.Users.Where(candidate =>
                candidate.NormalizedEmail == request.ExpectedNormalizedEmail))
            .ToListAsync(cancellationToken);
        if (exactEmailOwners.Count != 1 || exactEmailOwners[0].UserID != user.UserID)
        {
            return false;
        }

        var expectedAuditDetails = BootstrapAuditDetails(operationContext);
        return await _dbContext.AuditLogs
            .AsNoTracking()
            .AnyAsync(audit =>
                    audit.UserID == user.UserID
                    && audit.Action == BootstrapAuditAction
                    && audit.Details == expectedAuditDetails,
                cancellationToken);
    }

    private static async Task TryRollbackWithoutMaskingAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the original failure for execution-strategy handling.
        }
    }

    private async Task ValidateTargetAndConflictsAsync(
        User user,
        OwnerBootstrapRequest request,
        CancellationToken cancellationToken)
    {
        if (!user.IsActive
            || user.Role != UserRole.Administrator
            || user.MemberID.HasValue
            || !string.IsNullOrEmpty(user.FirebaseUid)
            || !EmailNormalization.TryNormalize(user.Email, out var targetEmail)
            || !string.Equals(
                targetEmail,
                request.ExpectedNormalizedEmail,
                StringComparison.Ordinal))
        {
            throw IdentityConflict();
        }

        var uidCandidates = await LockUsersByUidAsync(request.FirebaseUid, cancellationToken);
        if (uidCandidates.Count != 0)
        {
            throw IdentityConflict();
        }

        var boundOwnerExists = await (IsSqlServer
            ? _dbContext.Users.FromSqlRaw(
                "SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [Role] = 0 AND [FirebaseUid] IS NOT NULL")
            : _dbContext.Users.Where(candidate =>
                candidate.Role == UserRole.Administrator
                && candidate.FirebaseUid != null))
            .AnyAsync(cancellationToken);
        if (boundOwnerExists)
        {
            throw IdentityConflict();
        }

        var exactEmailOwners = await (IsSqlServer
            ? _dbContext.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [NormalizedEmail] = {request.ExpectedNormalizedEmail}")
            : _dbContext.Users.Where(candidate =>
                candidate.NormalizedEmail == request.ExpectedNormalizedEmail))
            .ToListAsync(cancellationToken);
        if (exactEmailOwners.Any(candidate => candidate.UserID != user.UserID))
        {
            throw IdentityConflict();
        }

        var unnormalizedOtherExists = await (IsSqlServer
            ? _dbContext.Users.FromSqlRaw(
                "SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [NormalizedEmail] IS NULL")
            : _dbContext.Users.Where(candidate => candidate.NormalizedEmail == null))
            .AnyAsync(candidate => candidate.UserID != user.UserID, cancellationToken);
        if (unnormalizedOtherExists)
        {
            throw IdentityConflict();
        }
    }

    private Task<User?> GetTargetForUpdateAsync(int userId, CancellationToken cancellationToken) =>
        (IsSqlServer
            ? _dbContext.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [UserID] = {userId}")
            : _dbContext.Users.Where(user => user.UserID == userId))
        .SingleOrDefaultAsync(cancellationToken);

    private async Task<List<User>> LockUsersByUidAsync(
        string firebaseUid,
        CancellationToken cancellationToken)
    {
        var candidates = await (IsSqlServer
            ? _dbContext.Users.FromSqlInterpolated(
                $"SELECT * FROM [Users] WITH (UPDLOCK, HOLDLOCK) WHERE [FirebaseUid] = {firebaseUid}")
            : _dbContext.Users.Where(user => user.FirebaseUid == firebaseUid))
            .Take(3)
            .ToListAsync(cancellationToken);
        return candidates
            .Where(candidate =>
                string.Equals(candidate.FirebaseUid, firebaseUid, StringComparison.Ordinal))
            .Take(2)
            .ToList();
    }

    private static void ValidateRequest(OwnerBootstrapRequest request)
    {
        if (request.UserId <= 0
            || !FirebaseIdentityValidation.TryValidateUid(request.FirebaseUid)
            || !EmailNormalization.TryNormalize(
                request.ExpectedNormalizedEmail,
                out var normalizedEmail)
            || !string.Equals(
                request.ExpectedNormalizedEmail,
                normalizedEmail,
                StringComparison.Ordinal)
            || request.DryRun == request.Confirm)
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "The bootstrap request is invalid.");
        }
    }

    private bool IsSqlServer => string.Equals(
        _dbContext.Database.ProviderName,
        "Microsoft.EntityFrameworkCore.SqlServer",
        StringComparison.Ordinal);

    private static string SafeCorrelation(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 100
        && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            ? value
            : "unavailable";

    private static string BootstrapAuditDetails(IdentityOperationContext operationContext) =>
        $"Initial owner identity bound; CorrelationId={SafeCorrelation(operationContext.CorrelationId)}.";

    private static string SafeIpAddress(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 50 ? value : "Unknown";

    private static AppAccessException IdentityConflict() => new(
        StatusCodes.Status409Conflict,
        ErrorCodes.IdentityConflict,
        "The identity operation conflicts with existing account state.");

    private static AppAccessException AccessForbidden() => new(
        StatusCodes.Status403Forbidden,
        "ACCESS_FORBIDDEN",
        "Access is forbidden.");
}
