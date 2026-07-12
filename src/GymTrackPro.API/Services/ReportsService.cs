using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.API.Authentication;
using GymTrackPro.Shared.Constants;
using System.Globalization;

namespace GymTrackPro.API.Services;

public class ReportsService : IReportsService
{
    private readonly GymDbContext _context;
    private readonly ITimezoneService _timezoneService;
    private readonly IClockService _clock;

    public ReportsService(
        GymDbContext context,
        ITimezoneService timezoneService,
        IClockService clock)
    {
        _context = context;
        _timezoneService = timezoneService;
        _clock = clock;
    }

    public async Task<IEnumerable<DailyRevenueReportDto>> GetDailyRevenueAsync(DateTime startDate, DateTime endDate)
    {
        var range = await GetLegacyInclusiveGymDateRangeAsync(startDate, endDate);
        var timeZone = await _timezoneService.GetGymTimeZoneAsync();
        var payments = await _context.Payments
            .AsNoTracking()
            .Where(payment => !payment.IsDeleted
                && payment.PaymentStatus == PaymentStatus.Paid
                && payment.DatePaid >= range.StartUtc
                && payment.DatePaid < range.EndExclusiveUtc)
            .Select(payment => new
            {
                payment.DatePaid,
                payment.Amount,
                payment.Discount,
                payment.FinalAmount
            })
            .ToListAsync();

        return payments
            .GroupBy(payment => TimeZoneInfo.ConvertTimeFromUtc(
                AsUtcInstant(payment.DatePaid),
                timeZone).Date)
            .Select(group => new DailyRevenueReportDto
            {
                Date = group.Key,
                TransactionCount = group.Count(),
                GrossAmount = group.Sum(payment => payment.Amount),
                TotalDiscount = group.Sum(payment => payment.Discount),
                NetAmount = group.Sum(payment => payment.FinalAmount)
            })
            .OrderBy(item => item.Date)
            .ToList();
    }

    public async Task<IEnumerable<MonthlyRevenueReportDto>> GetMonthlyRevenueAsync(DateTime startDate, DateTime endDate)
    {
        var range = await GetLegacyInclusiveGymDateRangeAsync(startDate, endDate);
        var timeZone = await _timezoneService.GetGymTimeZoneAsync();
        var payments = await _context.Payments
            .AsNoTracking()
            .Where(payment => !payment.IsDeleted
                && payment.PaymentStatus == PaymentStatus.Paid
                && payment.DatePaid >= range.StartUtc
                && payment.DatePaid < range.EndExclusiveUtc)
            .Select(payment => new
            {
                payment.DatePaid,
                payment.Amount,
                payment.Discount,
                payment.FinalAmount
            })
            .ToListAsync();

        return payments
            .GroupBy(payment =>
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(
                    AsUtcInstant(payment.DatePaid),
                    timeZone);
                return (local.Year, local.Month);
            })
            .Select(group => new MonthlyRevenueReportDto
            {
                Month = $"{group.Key.Year:D4}-{group.Key.Month:D2}",
                TransactionCount = group.Count(),
                GrossAmount = group.Sum(payment => payment.Amount),
                TotalDiscount = group.Sum(payment => payment.Discount),
                NetAmount = group.Sum(payment => payment.FinalAmount)
            })
            .OrderBy(item => item.Month)
            .ToList();
    }

    public async Task<IEnumerable<AttendanceReportDto>> GetAttendanceReportAsync(
        DateTime startDate,
        DateTime endDate)
    {
        var range = await GetLegacyInclusiveGymDateRangeAsync(startDate, endDate);

        var rows = await _context.AttendanceLogs
            .AsNoTracking()
            .Where(attendance => !attendance.IsVoided
                && attendance.AttendanceDate >= range.StartGymDate
                && attendance.AttendanceDate < range.EndExclusiveGymDate)
            .OrderBy(attendance => attendance.CheckInTime)
            .Select(attendance => new
            {
                attendance.AttendanceID,
                attendance.MemberID,
                attendance.AttendanceDate,
                attendance.CheckInTime,
                attendance.CheckOutTime,
                MemberName = attendance.Member == null
                    ? "Unknown"
                    : attendance.Member.FirstName + " " + attendance.Member.LastName
            })
            .ToListAsync();
        var memberIds = rows.Select(row => row.MemberID).Distinct().ToArray();
        var startStorageDate = GymMembershipPolicy.ToStorageDate(range.StartGymDate);
        var endStorageDate = GymMembershipPolicy.ToStorageDate(range.EndExclusiveGymDate);
        var subscriptionRows = await _context.Subscriptions
            .AsNoTracking()
            .Include(subscription => subscription.Plan)
            .Where(subscription => memberIds.Contains(subscription.MemberID)
                && subscription.StartDate < endStorageDate
                && subscription.EndDate >= startStorageDate)
            .Select(subscription => new
            {
                Subscription = subscription,
                HasOpenPause = _context.MembershipPauses.Any(pause =>
                    pause.SubscriptionID == subscription.SubscriptionID
                    && pause.PauseEndDate == null)
            })
            .ToListAsync();
        var subscriptionsByMember = subscriptionRows
            .GroupBy(row => row.Subscription.MemberID)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new MembershipCoverageCandidate(
                    row.Subscription,
                    row.HasOpenPause)).ToArray());

        return rows.Select(row => new AttendanceReportDto
        {
            AttendanceID = row.AttendanceID,
            MemberName = row.MemberName,
            PlanName = subscriptionsByMember.TryGetValue(row.MemberID, out var candidates)
                ? GymMembershipPolicy.SelectHistoricalCoverage(candidates, row.AttendanceDate)
                    ?.Plan?.PlanName ?? "Walk-in / None"
                : "Walk-in / None",
            CheckInTime = AsUtcInstant(row.CheckInTime),
            CheckOutTime = row.CheckOutTime.HasValue
                ? AsUtcInstant(row.CheckOutTime.Value)
                : null
        }).ToList();
    }

    public async Task<IEnumerable<MembershipSalesReportDto>> GetMembershipSalesReportAsync(DateTime startDate, DateTime endDate)
    {
        var range = await GetLegacyInclusiveGymDateRangeAsync(startDate, endDate);
        var rows = await _context.Payments
            .Include(p => p.Member)
            .Include(p => p.Subscription)
            .ThenInclude(s => s!.Plan)
            .Where(p => !p.IsDeleted
                && p.PaymentStatus == PaymentStatus.Paid
                && p.DatePaid >= range.StartUtc
                && p.DatePaid < range.EndExclusiveUtc)
            .OrderBy(p => p.DatePaid)
            .Select(p => new MembershipSalesReportDto
            {
                MemberName = p.Member != null ? $"{p.Member.FirstName} {p.Member.LastName}" : "Unknown",
                PlanName = p.Subscription != null && p.Subscription.Plan != null ? p.Subscription.Plan.PlanName : "Unknown",
                Amount = p.Amount,
                Discount = p.Discount,
                FinalAmount = p.FinalAmount,
                DatePaid = p.DatePaid,
                PaymentMethod = p.PaymentMethod.ToString()
            })
            .ToListAsync();
        foreach (var row in rows)
        {
            row.DatePaid = AsUtcInstant(row.DatePaid);
        }

        return rows;
    }

    public async Task<IEnumerable<ExpiringMembershipsReportDto>> GetExpiringMembershipsReportAsync(int nextDays)
    {
        if (nextDays is < 1 or > 366)
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidAttendanceRange,
                "The report range is invalid.");
        }

        var nowUtc = _clock.UtcNow;
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("The application clock must return UTC values.");
        }
        var today = await _timezoneService.GetGymDateAsync(nowUtc);
        var endExclusive = today.AddDays(nextDays);
        var todayStorage = GymMembershipPolicy.ToStorageDate(today);
        var tomorrowStorage = GymMembershipPolicy.ToStorageDate(today.AddDays(1));
        var rows = await _context.Subscriptions
            .AsNoTracking()
            .Include(subscription => subscription.Member)
            .Include(subscription => subscription.Plan)
            .Where(subscription => subscription.Status == GymMembershipPolicy.Active
                && subscription.StartDate < tomorrowStorage
                && subscription.EndDate >= todayStorage
                && subscription.Member != null
                && !subscription.Member.IsDeleted
                && subscription.Member.Status == GymMembershipPolicy.MemberActive)
            .Select(subscription => new
            {
                Subscription = subscription,
                HasOpenPause = _context.MembershipPauses.Any(pause =>
                    pause.SubscriptionID == subscription.SubscriptionID
                    && pause.PauseEndDate == null)
            })
            .ToListAsync();
        return rows
            .GroupBy(row => row.Subscription.MemberID)
            .Select(group => GymMembershipPolicy.SelectCurrentCoverage(
                group.Select(row => new MembershipCoverageCandidate(
                    row.Subscription,
                    row.HasOpenPause)),
                today))
            .Where(selection => selection.State == AttendanceMembershipState.Active
                && selection.Subscription is not null
                && GymMembershipPolicy.ToCalendarDate(selection.Subscription.EndDate) >= today
                && GymMembershipPolicy.ToCalendarDate(selection.Subscription.EndDate) < endExclusive)
            .Select(selection => selection.Subscription!)
            .OrderBy(subscription => subscription.EndDate)
            .Select(subscription => new ExpiringMembershipsReportDto
            {
                MemberName = subscription.Member is null
                    ? "Unknown"
                    : $"{subscription.Member.FirstName} {subscription.Member.LastName}",
                PlanName = subscription.Plan?.PlanName ?? "Unknown",
                StartDate = GymMembershipPolicy.NormalizeCalendarDate(subscription.StartDate),
                EndDate = GymMembershipPolicy.NormalizeCalendarDate(subscription.EndDate),
                Status = subscription.Status
            })
            .ToList();
    }

    public async Task<IEnumerable<RefundReportDto>> GetRefundReportAsync(DateTime startDate, DateTime endDate)
    {
        var range = await GetLegacyInclusiveGymDateRangeAsync(startDate, endDate);
        var rows = await _context.Payments
            .Include(p => p.Member)
            .Where(p => !p.IsDeleted
                && p.PaymentStatus == PaymentStatus.Refunded
                && p.LastModified >= range.StartUtc
                && p.LastModified < range.EndExclusiveUtc)
            .OrderBy(p => p.LastModified)
            .Select(p => new RefundReportDto
            {
                PaymentID = p.PaymentID,
                MemberName = p.Member != null ? $"{p.Member.FirstName} {p.Member.LastName}" : "Unknown",
                ReceiptNumber = p.ReceiptNumber,
                Amount = p.FinalAmount,
                DateRefunded = p.LastModified
            })
            .ToListAsync();
        foreach (var row in rows)
        {
            row.DateRefunded = AsUtcInstant(row.DateRefunded);
        }

        return rows;
    }

    public async Task<IEnumerable<CashierActivityReportDto>> GetCashierActivityReportAsync(DateTime startDate, DateTime endDate)
    {
        var range = await GetLegacyInclusiveGymDateRangeAsync(startDate, endDate);
        var rows = await _context.AuditLogs
            .Include(a => a.User)
            .Where(a => a.Timestamp >= range.StartUtc && a.Timestamp < range.EndExclusiveUtc)
            .OrderBy(a => a.Timestamp)
            .Select(a => new CashierActivityReportDto
            {
                Username = a.User != null ? a.User.Username : "System/Anonymous",
                Action = a.Action,
                Details = a.Details,
                Timestamp = a.Timestamp,
                IpAddress = a.IPAddress
            })
            .ToListAsync();
        foreach (var row in rows)
        {
            row.Timestamp = AsUtcInstant(row.Timestamp);
        }

        return rows;
    }

    public async Task<OwnerAttendanceSummaryDto> GetAttendanceSummaryAsync(
        DateOnly? fromGymDate,
        DateOnly? endExclusiveGymDate,
        string bucket)
    {
        if (!string.Equals(bucket, "day", StringComparison.Ordinal))
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.UnsupportedAttendancePreset,
                "Attendance summary supports only day buckets.");
        }

        var nowUtc = _clock.UtcNow;
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("The application clock must return UTC values.");
        }

        var today = await _timezoneService.GetGymDateAsync(nowUtc);
        DateOnly startGymDate;
        DateOnly endGymDateExclusive;
        if (!fromGymDate.HasValue && !endExclusiveGymDate.HasValue)
        {
            startGymDate = today.AddDays(-6);
            endGymDateExclusive = today.AddDays(1);
        }
        else if (fromGymDate.HasValue && endExclusiveGymDate.HasValue)
        {
            startGymDate = fromGymDate.Value;
            endGymDateExclusive = endExclusiveGymDate.Value;
        }
        else
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidAttendanceRange,
                "Both from and end-exclusive to dates are required.");
        }

        var rangeDays = endGymDateExclusive.DayNumber - startGymDate.DayNumber;
        if (rangeDays <= 0 || rangeDays > 366)
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidAttendanceRange,
                "The attendance summary range is invalid.");
        }
        var groupedCounts = await _context.AttendanceLogs
            .AsNoTracking()
            .Where(attendance => !attendance.IsVoided
                && attendance.AttendanceDate >= startGymDate
                && attendance.AttendanceDate < endGymDateExclusive)
            .GroupBy(attendance => attendance.AttendanceDate)
            .Select(group => new { Date = group.Key, Count = group.Count() })
            .ToListAsync();

        var countByDate = groupedCounts.ToDictionary(item => item.Date, item => item.Count);
        var points = new List<AttendanceTrendPointDto>(rangeDays);
        for (var date = startGymDate; date < endGymDateExclusive; date = date.AddDays(1))
        {
            points.Add(new AttendanceTrendPointDto
            {
                Date = date,
                Label = date.ToString("MMM d", CultureInfo.InvariantCulture),
                VisitCount = countByDate.GetValueOrDefault(date)
            });
        }

        var total = points.Sum(point => point.VisitCount);
        var utcRange = await _timezoneService.GetUtcRangeForGymDateRangeAsync(
            startGymDate,
            endGymDateExclusive);
        var timeZone = await _timezoneService.GetGymTimeZoneAsync();
        var dailyCounts = points.ToDictionary(
            point => point.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            point => point.VisitCount);

        return new OwnerAttendanceSummaryDto
        {
            Points = points,
            DailyCounts = dailyCounts,
            TotalVisits = total,
            AverageVisits = Math.Round((double)total / rangeDays, 2),
            FromDate = utcRange.StartUtc,
            ToDate = utcRange.EndExclusiveUtc,
            FromGymDate = startGymDate,
            EndExclusiveGymDate = endGymDateExclusive,
            PresetDays = rangeDays,
            Timezone = timeZone.Id,
            GeneratedAt = nowUtc
        };
    }

    private async Task<LegacyReportRange> GetLegacyInclusiveGymDateRangeAsync(
        DateTime startDate,
        DateTime endDate)
    {
        var startGymDate = DateOnly.FromDateTime(startDate);
        var endGymDate = DateOnly.FromDateTime(endDate);
        var inclusiveDays = endGymDate.DayNumber - startGymDate.DayNumber + 1;
        if (inclusiveDays <= 0 || inclusiveDays > 366)
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidAttendanceRange,
                "The report date range is invalid.");
        }

        if (endGymDate == DateOnly.MaxValue)
        {
            throw new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidAttendanceRange,
                "The report date range is outside the supported range.");
        }

        var utcRange = await _timezoneService.GetUtcRangeForGymDateRangeAsync(
            startGymDate,
            endGymDate.AddDays(1));
        return new LegacyReportRange(
            utcRange.StartUtc,
            utcRange.EndExclusiveUtc,
            startGymDate,
            endGymDate.AddDays(1));
    }

    private static DateTime AsUtcInstant(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private sealed record LegacyReportRange(
        DateTime StartUtc,
        DateTime EndExclusiveUtc,
        DateOnly StartGymDate,
        DateOnly EndExclusiveGymDate);
}
