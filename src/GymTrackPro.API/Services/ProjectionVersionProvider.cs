using System.Data;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GymTrackPro.API.Services;

/// <summary>
/// Uses the request-scoped DbContext so a projection version can participate in
/// the exact same transaction as its domain mutation.
/// </summary>
public sealed class ProjectionVersionProvider : IProjectionVersionProvider
{
    public const string AtomicIncrementSql =
        "UPDATE [MemberProjectionVersions] WITH (UPDLOCK, ROWLOCK) " +
        "SET [Version] = [Version] + 1 " +
        "OUTPUT INSERTED.[Version] " +
        "WHERE [MemberID] = @memberId AND [Version] < @maximumVersion";

    private readonly GymDbContext _dbContext;

    public ProjectionVersionProvider(GymDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<long> GetMutationVersionForMemberAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        ValidateMemberId(memberId);
        var version = await _dbContext.MemberProjectionVersions
            .AsNoTracking()
            .Where(item => item.MemberID == memberId)
            .Select(item => (long?)item.Version)
            .SingleOrDefaultAsync(cancellationToken);

        return version ?? throw MissingVersion(memberId);
    }

    public async Task<long> IncrementMutationVersionForMemberAsync(
        int memberId,
        CancellationToken cancellationToken = default)
    {
        ValidateMemberId(memberId);

        if (!_dbContext.Database.IsRelational())
        {
            var entity = await _dbContext.MemberProjectionVersions
                .SingleOrDefaultAsync(item => item.MemberID == memberId, cancellationToken)
                ?? throw MissingVersion(memberId);
            if (entity.Version >= MemberProjectionVersion.MaximumVersion)
            {
                throw VersionOverflow(memberId);
            }

            entity.Version = checked(entity.Version + 1);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return entity.Version;
        }

        if (!string.Equals(
                _dbContext.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.SqlServer",
                StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "Atomic member projection version allocation requires SQL Server.");
        }

        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A member projection version increment requires the caller's active domain transaction.");
        }

        PrepareTrackedVersionStateForAtomicIncrement(memberId);

        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandType = CommandType.Text;
            command.CommandText = AtomicIncrementSql;

            var memberParameter = command.CreateParameter();
            memberParameter.ParameterName = "@memberId";
            memberParameter.DbType = DbType.Int32;
            memberParameter.Value = memberId;
            command.Parameters.Add(memberParameter);

            var maximumParameter = command.CreateParameter();
            maximumParameter.ParameterName = "@maximumVersion";
            maximumParameter.DbType = DbType.Int64;
            maximumParameter.Value = MemberProjectionVersion.MaximumVersion;
            command.Parameters.Add(maximumParameter);

            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is not null and not DBNull)
            {
                return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }

        var current = await _dbContext.MemberProjectionVersions
            .AsNoTracking()
            .Where(item => item.MemberID == memberId)
            .Select(item => (long?)item.Version)
            .SingleOrDefaultAsync(cancellationToken);
        if (!current.HasValue)
        {
            throw MissingVersion(memberId);
        }

        throw VersionOverflow(memberId);
    }

    internal void PrepareTrackedVersionStateForAtomicIncrement(int memberId)
    {
        var trackedEntries = _dbContext.ChangeTracker
            .Entries<MemberProjectionVersion>()
            .Where(entry => entry.Entity.MemberID == memberId)
            .ToList();
        if (trackedEntries.Any(entry => entry.State is EntityState.Added
                or EntityState.Modified
                or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "A pending tracked projection version mutation cannot be combined with an atomic increment.");
        }

        foreach (var entry in trackedEntries)
        {
            entry.State = EntityState.Detached;
        }
    }

    private static void ValidateMemberId(int memberId)
    {
        if (memberId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memberId));
        }
    }

    private static InvalidOperationException MissingVersion(int memberId) => new(
        $"Member projection version state is unavailable for member {memberId}.");

    private static OverflowException VersionOverflow(int memberId) => new(
        $"Member projection version state is exhausted for member {memberId}.");
}
