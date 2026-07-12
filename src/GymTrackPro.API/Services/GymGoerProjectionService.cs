using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GymTrackPro.API.Authentication;
using GymTrackPro.Shared.Constants;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class GymGoerProjectionService : IGymGoerProjectionService
{
    public const string BadgeRuleVersion = "1.0";

    private readonly IMemberRepository _memberRepository;
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly ITimezoneService _timezoneService;
    private readonly IClockService _clock;
    private readonly ICurrentUserContext _currentUser;
    private readonly IProjectionVersionProvider _versionProvider;

    public GymGoerProjectionService(
        IMemberRepository memberRepository,
        IAttendanceRepository attendanceRepository,
        ITimezoneService timezoneService,
        IClockService clock,
        ICurrentUserContext currentUser,
        IProjectionVersionProvider versionProvider)
    {
        _memberRepository = memberRepository;
        _attendanceRepository = attendanceRepository;
        _timezoneService = timezoneService;
        _clock = clock;
        _currentUser = currentUser;
        _versionProvider = versionProvider;
    }

    public Task<GoerDashboardDto> GetGoerDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var memberId = RequireMemberId();
        return _attendanceRepository.ExecuteConsistentReadAsync(
            transactionToken => GetGoerDashboardCoreAsync(memberId, transactionToken),
            cancellationToken);
    }

    private async Task<GoerDashboardDto> GetGoerDashboardCoreAsync(
        int memberId,
        CancellationToken cancellationToken)
    {
        var authoritativeTimeZoneId = await RequireAuthoritativeTimeZoneIdAsync(cancellationToken);
        var member = await RequireMemberAsync(memberId);
        var nowUtc = GetUtcNow();
        var currentGymDate = await _timezoneService.GetGymDateAsync(
            nowUtc,
            authoritativeTimeZoneId,
            cancellationToken);
        var monthStart = new DateOnly(currentGymDate.Year, currentGymDate.Month, 1);
        var monthEndExclusive = monthStart.AddMonths(1);
        var monthRange = await _timezoneService.GetUtcRangeForGymDateRangeAsync(
            monthStart,
            monthEndExclusive,
            authoritativeTimeZoneId,
            cancellationToken);

        var distinctVisitDates = await _attendanceRepository.GetDistinctVisitDatesAsync(
            memberId,
            cancellationToken);
        var completedSessions = await _attendanceRepository.GetCompletedSessionsOverlappingAsync(
            memberId,
            monthRange.StartUtc,
            monthRange.EndExclusiveUtc,
            cancellationToken);
        var openSession = await _attendanceRepository.GetOpenSessionAsync(
            memberId,
            cancellationToken: cancellationToken);
        var membership = await _attendanceRepository.GetMembershipSnapshotAsync(
            memberId,
            currentGymDate,
            cancellationToken);
        var membershipState = string.Equals(
            member.Status,
            GymMembershipPolicy.MemberActive,
            StringComparison.Ordinal)
            ? membership.State
            : AttendanceMembershipState.Inactive;

        var metrics = AttendanceMetricsCalculator.Calculate(
            distinctVisitDates,
            completedSessions,
            monthStart,
            monthEndExclusive,
            monthRange,
            currentGymDate);
        var timeZone = await _timezoneService.GetGymTimeZoneAsync(
            authoritativeTimeZoneId,
            cancellationToken);
        var dataVersion = await GetDataVersionAsync(memberId, cancellationToken);
        var currentSession = openSession is null ? null : MapAttendance(openSession, member);
        var metadata = CreateMetadata(
            timeZone,
            nowUtc,
            dataVersion,
            currentGymDate,
            NormalizePersistedUtc(member.LastModified),
            membershipState,
            CanonicalizeAttendance(currentSession),
            metrics.CompletedDurationSeconds,
            metrics.PeriodVisitCount,
            metrics.CurrentStreak,
            metrics.LongestStreak,
            CanonicalizeBadges(metrics.Badges));

        return new GoerDashboardDto
        {
            Metadata = metadata,
            MembershipStatus = membershipState.ToString(),
            CurrentSession = currentSession,
            CurrentMonthDurationSeconds = metrics.CompletedDurationSeconds,
            CurrentMonthMinutes = RoundSecondsToMinutes(metrics.CompletedDurationSeconds),
            VisitCount = metrics.PeriodVisitCount,
            CurrentStreak = metrics.CurrentStreak,
            LongestStreak = metrics.LongestStreak,
            Badges = metrics.Badges,
            // One-release mobile compatibility adapter; remove after 2027-01-12.
            UnlockedBadges = metrics.Badges
                .Where(badge => badge.IsUnlocked)
                .Select(badge => badge.BadgeId)
                .ToList(),
            Timezone = timeZone.Id,
            GeneratedAt = nowUtc
        };
    }

    public Task<GoerDigitalCardDto> GetDigitalCardAsync(
        CancellationToken cancellationToken = default)
    {
        var memberId = RequireMemberId();
        return _attendanceRepository.ExecuteConsistentReadAsync(
            transactionToken => GetDigitalCardCoreAsync(memberId, transactionToken),
            cancellationToken);
    }

    private async Task<GoerDigitalCardDto> GetDigitalCardCoreAsync(
        int memberId,
        CancellationToken cancellationToken)
    {
        var authoritativeTimeZoneId = await RequireAuthoritativeTimeZoneIdAsync(cancellationToken);
        var member = await RequireMemberAsync(memberId);
        var nowUtc = GetUtcNow();
        var gymDate = await _timezoneService.GetGymDateAsync(
            nowUtc,
            authoritativeTimeZoneId,
            cancellationToken);
        var membership = await _attendanceRepository.GetMembershipSnapshotAsync(
            memberId,
            gymDate,
            cancellationToken);
        var membershipState = string.Equals(
            member.Status,
            GymMembershipPolicy.MemberActive,
            StringComparison.Ordinal)
            ? membership.State
            : AttendanceMembershipState.Inactive;
        var membershipExpiry = membership.ExpiryDate;
        var timeZone = await _timezoneService.GetGymTimeZoneAsync(
            authoritativeTimeZoneId,
            cancellationToken);
        var dataVersion = await GetDataVersionAsync(memberId, cancellationToken);
        var metadata = CreateMetadata(
            timeZone,
            nowUtc,
            dataVersion,
            gymDate,
            NormalizePersistedUtc(member.LastModified),
            membershipState,
            membershipExpiry?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            HashValue(member.QRCode));

        return new GoerDigitalCardDto
        {
            Metadata = metadata,
            MemberId = member.MemberID,
            MembershipStatus = membershipState.ToString(),
            ExpiryDate = membershipExpiry,
            QrCodeValue = member.QRCode
        };
    }

    public Task<GoerProgressDto> GetProgressAsync(
        string month,
        CancellationToken cancellationToken = default)
    {
        var memberId = RequireMemberId();
        return _attendanceRepository.ExecuteConsistentReadAsync(
            transactionToken => GetProgressCoreAsync(memberId, month, transactionToken),
            cancellationToken);
    }

    private async Task<GoerProgressDto> GetProgressCoreAsync(
        int memberId,
        string month,
        CancellationToken cancellationToken)
    {
        var authoritativeTimeZoneId = await RequireAuthoritativeTimeZoneIdAsync(cancellationToken);
        _ = await RequireMemberAsync(memberId);
        var nowUtc = GetUtcNow();
        var currentGymDate = await _timezoneService.GetGymDateAsync(
            nowUtc,
            authoritativeTimeZoneId,
            cancellationToken);
        var monthStart = ParseMonth(month, currentGymDate);
        var monthEndExclusive = monthStart.AddMonths(1);
        var monthRange = await _timezoneService.GetUtcRangeForGymDateRangeAsync(
            monthStart,
            monthEndExclusive,
            authoritativeTimeZoneId,
            cancellationToken);

        var distinctVisitDates = await _attendanceRepository.GetDistinctVisitDatesAsync(
            memberId,
            cancellationToken);
        var completedSessions = await _attendanceRepository.GetCompletedSessionsOverlappingAsync(
            memberId,
            monthRange.StartUtc,
            monthRange.EndExclusiveUtc,
            cancellationToken);

        var metrics = AttendanceMetricsCalculator.Calculate(
            distinctVisitDates,
            completedSessions,
            monthStart,
            monthEndExclusive,
            monthRange,
            currentGymDate);
        var timeZone = await _timezoneService.GetGymTimeZoneAsync(
            authoritativeTimeZoneId,
            cancellationToken);
        var dataVersion = await GetDataVersionAsync(memberId, cancellationToken);
        var metadata = CreateMetadata(
            timeZone,
            nowUtc,
            dataVersion,
            currentGymDate,
            monthStart,
            metrics.CompletedDurationSeconds,
            metrics.PeriodVisitCount,
            metrics.CurrentStreak,
            metrics.LongestStreak,
            BadgeRuleVersion,
            CanonicalizeBadges(metrics.Badges));

        return new GoerProgressDto
        {
            Metadata = metadata,
            MonthlyDurationSeconds = metrics.CompletedDurationSeconds,
            MonthlyDurationMinutes = RoundSecondsToMinutes(metrics.CompletedDurationSeconds),
            MonthlyVisits = metrics.PeriodVisitCount,
            CurrentStreak = metrics.CurrentStreak,
            LongestStreak = metrics.LongestStreak,
            Badges = metrics.Badges,
            // One-release mobile compatibility adapter; remove after 2027-01-12.
            EligibleBadges = metrics.Badges
                .Where(badge => badge.IsUnlocked)
                .Select(badge => badge.BadgeId)
                .ToList(),
            BadgeRuleVersion = BadgeRuleVersion
        };
    }

    private int RequireMemberId()
    {
        return _currentUser.MemberId is > 0
            ? _currentUser.MemberId.Value
            : throw new AppAccessException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.AccessForbidden,
                "Access is forbidden.");
    }

    private async Task<Member> RequireMemberAsync(int memberId)
    {
        var member = await _memberRepository.GetByIdAsync(memberId);
        if (member is null)
        {
            throw new AppAccessException(
                StatusCodes.Status404NotFound,
                ErrorCodes.MemberInactive,
                "The linked member is unavailable.");
        }

        return member;
    }

    private DateTime GetUtcNow()
    {
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("The application clock must return UTC values.");
        }

        return now;
    }

    private async Task<string> RequireAuthoritativeTimeZoneIdAsync(
        CancellationToken cancellationToken)
    {
        var timeZoneId = await _attendanceRepository.GetTimezoneIdForAttendanceWriteAsync(
            cancellationToken);
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            // Route all missing/blank/invalid values through the fail-closed resolver.
            _ = await _timezoneService.GetGymTimeZoneAsync(
                timeZoneId ?? string.Empty,
                cancellationToken);
        }

        return timeZoneId!.Trim();
    }

    private async Task<long> GetDataVersionAsync(
        int memberId,
        CancellationToken cancellationToken)
    {
        var mutationVersion = await _versionProvider.GetMutationVersionForMemberAsync(
            memberId,
            cancellationToken);
        if (mutationVersion < 0)
        {
            throw new InvalidOperationException(
                "The projection version provider must return a non-negative member mutation version.");
        }

        return mutationVersion;
    }

    private static DateOnly ParseMonth(string month, DateOnly currentGymDate)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            return new DateOnly(currentGymDate.Year, currentGymDate.Month, 1);
        }

        if (!DateTime.TryParseExact(
            month,
            "yyyy-MM",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidAttendanceRange,
                "The month must use YYYY-MM format.");
        }

        if (parsed.Year == DateOnly.MaxValue.Year
            && parsed.Month == DateOnly.MaxValue.Month)
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidAttendanceRange,
                "The month is outside the supported range.");
        }

        return new DateOnly(parsed.Year, parsed.Month, 1);
    }

    private static int RoundSecondsToMinutes(long seconds)
    {
        return checked((int)Math.Round(
            seconds / 60d,
            MidpointRounding.AwayFromZero));
    }

    private static ProjectionMetadataDto CreateMetadata(
        TimeZoneInfo timeZone,
        DateTime generatedAtUtc,
        long mutationVersion,
        DateOnly effectiveGymDate,
        params object?[] versionComponents)
    {
        var components = new[]
        {
            "schema=gym-goer-v1",
            $"timezone={timeZone.Id}",
            $"effectiveGymDate={effectiveGymDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
        }.Concat(versionComponents.Select(FormatVersionComponent));
        var canonical = new StringBuilder();
        foreach (var component in components)
        {
            canonical
                .Append(component.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(component)
                .Append('\n');
        }
        return new ProjectionMetadataDto
        {
            SchemaVersion = "gym-goer-v1",
            DataVersion = ProjectionVersionComposer.Compose(mutationVersion, effectiveGymDate),
            ContentETag = HashValue(canonical.ToString()),
            Timezone = timeZone.Id,
            GeneratedAtUtc = generatedAtUtc,
            CacheFreshUntilUtc = generatedAtUtc.AddMinutes(15)
        };
    }

    private static string HashValue(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string FormatVersionComponent(object? component)
    {
        return component switch
        {
            null => "null",
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => component.ToString() ?? "null"
        };
    }

    private static string CanonicalizeAttendance(AttendanceDto? attendance)
    {
        if (attendance is null)
        {
            return "null";
        }

        return string.Join(
            "\u001f",
            attendance.AttendanceID.ToString(CultureInfo.InvariantCulture),
            attendance.MemberID.ToString(CultureInfo.InvariantCulture),
            attendance.MemberName,
            attendance.AttendanceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            attendance.CheckInTime.ToString("O", CultureInfo.InvariantCulture),
            attendance.CheckOutTime?.ToString("O", CultureInfo.InvariantCulture) ?? "null",
            attendance.Source,
            attendance.IsVoided ? "1" : "0",
            attendance.SupersededByAttendanceID?.ToString(CultureInfo.InvariantCulture) ?? "null",
            attendance.LastModified.ToString("O", CultureInfo.InvariantCulture));
    }

    private static string CanonicalizeBadges(IEnumerable<BadgeEligibilityDto> badges)
    {
        return string.Join(
            "\u001f",
            badges.Select(badge => string.Join(
                "\u001e",
                badge.BadgeId,
                badge.IsUnlocked ? "1" : "0",
                badge.DerivedUnlockDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "null")));
    }

    private static AttendanceDto MapAttendance(Attendance attendance, Member member)
    {
        return new AttendanceDto
        {
            AttendanceID = attendance.AttendanceID,
            MemberID = attendance.MemberID,
            MemberName = $"{member.FirstName} {member.LastName}",
            AttendanceDate = attendance.AttendanceDate,
            CheckInTime = NormalizePersistedUtc(attendance.CheckInTime),
            CheckOutTime = attendance.CheckOutTime.HasValue
                ? NormalizePersistedUtc(attendance.CheckOutTime.Value)
                : null,
            Source = attendance.Source ?? string.Empty,
            LastModified = NormalizePersistedUtc(attendance.LastModified)
        };
    }

    private static DateTime NormalizePersistedUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => throw new InvalidOperationException("A persisted UTC timestamp had an invalid DateTime kind.")
        };
    }
}

public static class ProjectionVersionComposer
{
    public const int GymDateBits = 22;
    public const long MaximumMutationVersion = long.MaxValue >> GymDateBits;

    public static long Compose(long mutationVersion, DateOnly effectiveGymDate)
    {
        if (mutationVersion < 0 || mutationVersion > MaximumMutationVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(mutationVersion));
        }

        var dayNumber = effectiveGymDate.DayNumber;
        if (dayNumber < 0 || dayNumber >= (1 << GymDateBits))
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveGymDate));
        }

        return checked((mutationVersion << GymDateBits) | (uint)dayNumber);
    }
}

public sealed record AttendanceMetrics(
    long CompletedDurationSeconds,
    int PeriodVisitCount,
    int CurrentStreak,
    int LongestStreak,
    List<BadgeEligibilityDto> Badges);

public static class AttendanceMetricsCalculator
{
    public static AttendanceMetrics Calculate(
        IReadOnlyList<DateOnly> visitDates,
        IReadOnlyList<Attendance> completedSessions,
        DateOnly periodStart,
        DateOnly periodEndExclusive,
        UtcDateRange utcPeriod,
        DateOnly today)
    {
        var distinctDates = visitDates
            .Distinct()
            .OrderBy(date => date)
            .ToArray();
        var durationSeconds = CalculateOverlapSeconds(completedSessions, utcPeriod);
        var periodVisits = distinctDates.Count(date => date >= periodStart && date < periodEndExclusive);
        var longestStreak = CalculateLongestStreak(distinctDates);
        var currentStreak = CalculateCurrentStreak(distinctDates, today);
        var badges = CalculateBadges(distinctDates);

        return new AttendanceMetrics(
            durationSeconds,
            periodVisits,
            currentStreak,
            longestStreak,
            badges);
    }

    private static long CalculateOverlapSeconds(
        IEnumerable<Attendance> sessions,
        UtcDateRange period)
    {
        long totalSeconds = 0;
        foreach (var session in sessions)
        {
            if (!session.CheckOutTime.HasValue || session.IsVoided)
            {
                continue;
            }

            var overlapStart = session.CheckInTime > period.StartUtc
                ? session.CheckInTime
                : period.StartUtc;
            var overlapEnd = session.CheckOutTime.Value < period.EndExclusiveUtc
                ? session.CheckOutTime.Value
                : period.EndExclusiveUtc;
            if (overlapEnd > overlapStart)
            {
                totalSeconds = checked(totalSeconds + (long)Math.Floor((overlapEnd - overlapStart).TotalSeconds));
            }
        }

        return totalSeconds;
    }

    private static int CalculateLongestStreak(IReadOnlyList<DateOnly> dates)
    {
        if (dates.Count == 0)
        {
            return 0;
        }

        var longest = 1;
        var current = 1;
        for (var index = 1; index < dates.Count; index++)
        {
            current = dates[index].DayNumber - dates[index - 1].DayNumber == 1
                ? current + 1
                : 1;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    private static int CalculateCurrentStreak(IReadOnlyList<DateOnly> dates, DateOnly today)
    {
        if (dates.Count == 0)
        {
            return 0;
        }

        var latest = dates[^1];
        if (latest != today && latest != today.AddDays(-1))
        {
            return 0;
        }

        var streak = 1;
        for (var index = dates.Count - 1; index > 0; index--)
        {
            if (dates[index].DayNumber - dates[index - 1].DayNumber != 1)
            {
                break;
            }

            streak++;
        }

        return streak;
    }

    private static List<BadgeEligibilityDto> CalculateBadges(IReadOnlyList<DateOnly> dates)
    {
        DateOnly? firstVisitDate = dates.Count == 0 ? null : dates[0];
        DateOnly? threeDayStreakDate = null;
        var runLength = 1;
        for (var index = 1; index < dates.Count; index++)
        {
            runLength = dates[index].DayNumber - dates[index - 1].DayNumber == 1
                ? runLength + 1
                : 1;
            if (runLength == 3)
            {
                threeDayStreakDate = dates[index];
                break;
            }
        }

        var dateSet = dates.ToHashSet();
        var weekendWarriorDate = dates
            .Where(date => date.DayOfWeek == DayOfWeek.Saturday)
            .Select(date => date.AddDays(1))
            .Where(sunday => sunday.DayOfWeek == DayOfWeek.Sunday && dateSet.Contains(sunday))
            .Select(sunday => (DateOnly?)sunday)
            .FirstOrDefault();

        return new List<BadgeEligibilityDto>
        {
            CreateBadge("first_visit", firstVisitDate),
            CreateBadge("streak_3", threeDayStreakDate),
            CreateBadge("weekend_warrior", weekendWarriorDate)
        };
    }

    private static BadgeEligibilityDto CreateBadge(string badgeId, DateOnly? unlockDate)
    {
        return new BadgeEligibilityDto
        {
            BadgeId = badgeId,
            IsUnlocked = unlockDate.HasValue,
            DerivedUnlockDate = unlockDate
        };
    }
}
