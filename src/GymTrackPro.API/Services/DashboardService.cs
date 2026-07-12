using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class DashboardService : IDashboardService
{
    private const int DefaultStaleSessionHours = 16;
    private readonly GymDbContext _context;
    private readonly ITimezoneService _timezoneService;
    private readonly IClockService _clock;
    private readonly ISystemSettingService _settingService;

    public DashboardService(
        GymDbContext context,
        ITimezoneService timezoneService,
        IClockService clock,
        ISystemSettingService settingService)
    {
        _context = context;
        _timezoneService = timezoneService;
        _clock = clock;
        _settingService = settingService;
    }

    public async Task<DashboardMetricsDto> GetDashboardMetricsAsync()
    {
        var nowUtc = _clock.UtcNow;
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("The application clock must return UTC values.");
        }

        var gymDate = await _timezoneService.GetGymDateAsync(nowUtc);
        var dayRange = await _timezoneService.GetUtcRangeForGymDateAsync(gymDate);
        var monthStart = new DateOnly(gymDate.Year, gymDate.Month, 1);
        var monthRange = await _timezoneService.GetUtcRangeForGymDateRangeAsync(
            monthStart,
            monthStart.AddMonths(1));
        var timeZone = await _timezoneService.GetGymTimeZoneAsync();
        var staleSessionHours = await GetStaleSessionHoursAsync();
        var staleCutoffUtc = nowUtc.AddHours(-staleSessionHours);
        var sevenDaysAgoUtc = nowUtc.AddDays(-7);
        var gymDayStart = GymMembershipPolicy.ToStorageDate(gymDate);
        var gymDayEndExclusive = GymMembershipPolicy.ToStorageDate(gymDate.AddDays(1));
        var expiryEndExclusive = gymDate.AddDays(7);

        var openSessions = await _context.AttendanceLogs
            .AsNoTracking()
            .CountAsync(attendance => !attendance.IsVoided && attendance.CheckOutTime == null);
        var staleOpenSessions = await _context.AttendanceLogs
            .AsNoTracking()
            .CountAsync(attendance => !attendance.IsVoided
                && attendance.CheckOutTime == null
                && attendance.CheckInTime < staleCutoffUtc);
        var visitsToday = await _context.AttendanceLogs
            .AsNoTracking()
            .CountAsync(attendance => !attendance.IsVoided && attendance.AttendanceDate == gymDate);
        var effectiveActiveMemberships = _context.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.Status == GymMembershipPolicy.Active
                && subscription.StartDate < gymDayEndExclusive
                && subscription.EndDate >= gymDayStart
                && subscription.Member != null
                && !subscription.Member.IsDeleted
                && subscription.Member.Status == GymMembershipPolicy.MemberActive
                && !_context.MembershipPauses.Any(pause =>
                    pause.SubscriptionID == subscription.SubscriptionID
                    && pause.PauseEndDate == null));
        var activeMemberships = await effectiveActiveMemberships
            .Select(subscription => subscription.MemberID)
            .Distinct()
            .CountAsync();
        var revenueToday = await _context.Payments
            .AsNoTracking()
            .Where(payment => !payment.IsDeleted
                && payment.PaymentStatus == PaymentStatus.Paid
                && payment.DatePaid >= dayRange.StartUtc
                && payment.DatePaid < dayRange.EndExclusiveUtc)
            .SumAsync(payment => (decimal?)payment.FinalAmount);
        var revenueThisMonth = await _context.Payments
            .AsNoTracking()
            .Where(payment => !payment.IsDeleted
                && payment.PaymentStatus == PaymentStatus.Paid
                && payment.DatePaid >= monthRange.StartUtc
                && payment.DatePaid < monthRange.EndExclusiveUtc)
            .SumAsync(payment => (decimal?)payment.FinalAmount);
        var selectedExpiryDates = await effectiveActiveMemberships
            .GroupBy(subscription => subscription.MemberID)
            .Select(group => group.Max(subscription => subscription.EndDate))
            .ToListAsync();
        var expiringMemberships = selectedExpiryDates.Count(endDate =>
        {
            var expiryDate = GymMembershipPolicy.ToCalendarDate(endDate);
            return expiryDate >= gymDate && expiryDate < expiryEndExclusive;
        });
        var newRegistrations = await _context.Members
            .AsNoTracking()
            .CountAsync(member => !member.IsDeleted && member.DateRegistered >= sevenDaysAgoUtc);
        var checkInTimes = await _context.AttendanceLogs
            .AsNoTracking()
            .Where(attendance => !attendance.IsVoided
                && attendance.CheckInTime >= dayRange.StartUtc
                && attendance.CheckInTime < dayRange.EndExclusiveUtc)
            .Select(attendance => attendance.CheckInTime)
            .ToListAsync();
        var revenueByPlan = await _context.Payments
            .AsNoTracking()
            .Where(payment => !payment.IsDeleted && payment.PaymentStatus == PaymentStatus.Paid)
            .GroupBy(payment => payment.Subscription != null && payment.Subscription.Plan != null
                ? payment.Subscription.Plan.PlanName
                : "Unknown Plan")
            .Select(group => new PlanRevenueDto
            {
                PlanName = group.Key,
                Revenue = group.Sum(payment => payment.FinalAmount)
            })
            .OrderByDescending(item => item.Revenue)
            .ToListAsync();

        var countsByHour = checkInTimes
            .GroupBy(checkInUtc => TimeZoneInfo.ConvertTimeFromUtc(checkInUtc, timeZone).Hour)
            .ToDictionary(group => group.Key, group => group.Count());
        var hourlyCheckIns = Enumerable.Range(0, 24)
            .Select(hour => new HourlyCheckInDto
            {
                Hour = hour,
                Count = countsByHour.GetValueOrDefault(hour)
            })
            .ToList();

        return new DashboardMetricsDto
        {
            MembersCheckedInCount = openSessions,
            MembersCheckedInLabel = "Open sessions",
            VisitsTodayCount = visitsToday,
            StaleOpenSessionCount = staleOpenSessions,
            StaleSessionThresholdHours = staleSessionHours,
            ActiveMembershipsCount = activeMemberships,
            RevenueToday = revenueToday ?? 0m,
            RevenueThisMonth = revenueThisMonth ?? 0m,
            ExpiringMembershipsCount = expiringMemberships,
            NewRegistrationsCount = newRegistrations,
            CheckInsByHour = hourlyCheckIns,
            RevenueByPlan = revenueByPlan
        };
    }

    private async Task<int> GetStaleSessionHoursAsync()
    {
        return await _settingService.GetValueIntAsync(
            SystemSettingService.StaleSessionHoursKey,
            DefaultStaleSessionHours);
    }
}
