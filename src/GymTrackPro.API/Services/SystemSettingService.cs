using System.Globalization;
using System.Data;
using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GymTrackPro.API.Services;

public class SystemSettingService : ISystemSettingService
{
    public const string StaleSessionHoursKey = "StaleSessionHours";
    public const int DefaultStaleSessionHours = 16;
    public const int MaximumStaleSessionHours = 168;

    private readonly ISystemSettingRepository _repository;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUserContext _currentUser;
    private readonly GymDbContext _dbContext;
    private readonly ITimezoneService _timezoneService;

    public SystemSettingService(
        ISystemSettingRepository repository,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        ICurrentUserContext currentUser,
        GymDbContext dbContext,
        ITimezoneService timezoneService)
    {
        _repository = repository;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
        _dbContext = dbContext;
        _timezoneService = timezoneService;
    }

    private int GetRequiredCurrentUserId() => _currentUser.UserId
        ?? throw new UnauthorizedAccessException("An active application user is required.");

    private string GetClientIpAddress() =>
        _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

    public async Task<string> GetValueAsync(string key, string defaultValue = "")
    {
        var setting = await _repository.GetByKeyAsync(key);
        return setting?.SettingValue ?? defaultValue;
    }

    public async Task<int> GetValueIntAsync(string key, int defaultValue = 0)
    {
        var value = await GetValueAsync(key, null!);
        if (string.Equals(key, StaleSessionHoursKey, StringComparison.Ordinal))
        {
            return ParseStaleSessionHours(value, StatusCodes.Status503ServiceUnavailable);
        }

        return value is not null
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : defaultValue;
    }

    public async Task<long> GetValueLongAsync(string key, long defaultValue = 0L)
    {
        var value = await GetValueAsync(key, null!);
        return value is not null
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
                ? result
                : defaultValue;
    }

    public async Task<double> GetValueDoubleAsync(string key, double defaultValue = 0.0)
    {
        var value = await GetValueAsync(key, null!);
        return value is not null
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                ? result
                : defaultValue;
    }

    public async Task<bool> GetValueBoolAsync(string key, bool defaultValue = false)
    {
        var value = await GetValueAsync(key, null!);
        return value is not null && bool.TryParse(value, out var result) ? result : defaultValue;
    }

    public async Task<IEnumerable<SystemSettingDto>> GetAllSettingsAsync()
    {
        var settings = await _repository.GetAllAsync();
        return settings.Select(setting => new SystemSettingDto
        {
            SettingKey = setting.SettingKey,
            SettingValue = setting.SettingValue,
            GroupName = setting.GroupName,
            Description = setting.Description,
            LastModified = setting.LastModified
        }).ToList();
    }

    public async Task UpdateSettingAsync(string key, string value)
    {
        var actorUserId = GetRequiredCurrentUserId();
        if (string.Equals(key, TimezoneService.TimezoneSettingKey, StringComparison.Ordinal))
        {
            var normalizedTimezone = ValidateTimezoneId(value);
            await UpdateTimezoneAtomicallyAsync(
                normalizedTimezone,
                actorUserId,
                GetClientIpAddress());
            _timezoneService.InvalidateCache();
            return;
        }

        var setting = await _repository.GetByKeyAsync(key);
        if (setting is null)
        {
            throw new KeyNotFoundException($"System setting with key '{key}' was not found.");
        }

        var normalizedValue = value;
        if (string.Equals(key, StaleSessionHoursKey, StringComparison.Ordinal))
        {
            normalizedValue = ParseStaleSessionHours(
                    value,
                    StatusCodes.Status400BadRequest)
                .ToString(CultureInfo.InvariantCulture);
        }

        setting.SettingValue = normalizedValue;
        setting.LastModified = DateTime.UtcNow;
        await _repository.UpdateAsync(setting);

        await _auditService.LogActivityAsync(
            actorUserId,
            "System Setting Modified",
            $"Configuration key '{key}' was modified.",
            GetClientIpAddress());
    }

    private async Task UpdateTimezoneAtomicallyAsync(
        string normalizedTimezone,
        int actorUserId,
        string ipAddress)
    {
        var operationId = Guid.NewGuid();
        var auditDetails =
            $"Configuration key '{TimezoneService.TimezoneSettingKey}' was modified. OperationId:{operationId:D}.";
        await ExecuteVerifiedSerializableAsync(
            operationId,
            async cancellationToken =>
        {
            // Lock order is fixed across configuration and attendance writers: the
            // Timezone setting key range first, followed by AttendanceLogs.
            var setting = await GetSettingForUpdateAsync(
                TimezoneService.TimezoneSettingKey,
                cancellationToken);
            if (setting is null)
            {
                throw new KeyNotFoundException(
                    $"System setting with key '{TimezoneService.TimezoneSettingKey}' was not found.");
            }

            // HOLDLOCK protects the empty attendance range so the first attendance insert
            // cannot race a timezone change after the configuration key is locked.
            var attendanceExists = await HasAttendanceForUpdateAsync(cancellationToken);

            var changed = !string.Equals(
                setting.SettingValue,
                normalizedTimezone,
                StringComparison.Ordinal);
            if (attendanceExists && changed)
            {
                throw new AppAccessException(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.AttendanceConflict,
                    "The gym timezone cannot change after attendance has been recorded.");
            }

            setting.SettingValue = normalizedTimezone;
            setting.LastModified = DateTime.UtcNow;
            _dbContext.AuditLogs.Add(new AuditLog
            {
                UserID = actorUserId,
                Action = "System Setting Modified",
                Details = auditDetails,
                IPAddress = SafeIpAddress(ipAddress),
                Timestamp = DateTime.UtcNow
            });
            return true;
        },
            async (id, cancellationToken) =>
            {
                var marker = $"Configuration key '{TimezoneService.TimezoneSettingKey}' was modified. OperationId:{id:D}.";
                return await _dbContext.AuditLogs
                    .AsNoTracking()
                    .AnyAsync(
                        audit => audit.Action == "System Setting Modified"
                            && audit.Details == marker,
                        cancellationToken);
            });
    }

    private async Task<T> ExecuteVerifiedSerializableAsync<T>(
        Guid operationId,
        Func<CancellationToken, Task<T>> operation,
        Func<Guid, CancellationToken, Task<bool>> verifySucceeded,
        CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational())
        {
            try
            {
                var result = await operation(cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch
            {
                _dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteInTransactionAsync(
                operationId,
                async (_, transactionToken) =>
                {
                    _dbContext.ChangeTracker.Clear();
                    var result = await operation(transactionToken);
                    await _dbContext.SaveChangesAsync(transactionToken);
                    return result;
                },
                async (id, verificationToken) =>
                {
                    _dbContext.ChangeTracker.Clear();
                    return await verifySucceeded(id, verificationToken);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    protected virtual Task<SystemSetting?> GetSettingForUpdateAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var local = _dbContext.SystemSettings.Local.FirstOrDefault(setting =>
            string.Equals(setting.SettingKey, key, StringComparison.Ordinal));
        if (local is not null)
        {
            _dbContext.Entry(local).State = EntityState.Detached;
        }

        return (IsSqlServer
                ? _dbContext.SystemSettings.FromSqlInterpolated(
                    $"SELECT * FROM [SystemSettings] WITH (UPDLOCK, HOLDLOCK) WHERE [SettingKey] = {key}")
                : _dbContext.SystemSettings.Where(setting => setting.SettingKey == key))
            .SingleOrDefaultAsync(cancellationToken);
    }

    protected virtual async Task<bool> HasAttendanceForUpdateAsync(CancellationToken cancellationToken)
    {
        if (!IsSqlServer)
        {
            return await _dbContext.AttendanceLogs.AnyAsync(cancellationToken);
        }

        var lockedRows = await _dbContext.AttendanceLogs.FromSqlRaw(
                "SELECT TOP (1) * FROM [AttendanceLogs] WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [AttendanceID] >= 0 ORDER BY [AttendanceID]")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return lockedRows.Count != 0;
    }

    private bool IsSqlServer => string.Equals(
        _dbContext.Database.ProviderName,
        "Microsoft.EntityFrameworkCore.SqlServer",
        StringComparison.Ordinal);

    private static string SafeIpAddress(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 50 ? value : "Unknown";

    private static int ParseStaleSessionHours(string? value, int statusCode)
    {
        if (value is null
            || !int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed is < 1 or > MaximumStaleSessionHours)
        {
            throw new AppAccessException(
                statusCode,
                ErrorCodes.AttendanceConfigurationInvalid,
                statusCode == StatusCodes.Status400BadRequest
                    ? "The attendance setting value is invalid."
                    : "The attendance configuration is unavailable.");
        }

        return parsed;
    }

    private static string ValidateTimezoneId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 100
            || value.Any(char.IsControl))
        {
            throw InvalidTimezoneValue();
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(value.Trim()).Id;
        }
        catch (TimeZoneNotFoundException)
        {
            throw InvalidTimezoneValue();
        }
        catch (InvalidTimeZoneException)
        {
            throw InvalidTimezoneValue();
        }
        catch (ArgumentException)
        {
            throw InvalidTimezoneValue();
        }
    }

    private static AppAccessException InvalidTimezoneValue() => new(
        StatusCodes.Status400BadRequest,
        ErrorCodes.GymTimezoneInvalid,
        "The gym timezone identifier is invalid.");
}
