using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class ReportsService : IReportsService
{
    private readonly GymDbContext _context;

    public ReportsService(GymDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<DailyRevenueReportDto>> GetDailyRevenueAsync(DateTime startDate, DateTime endDate)
    {
        var rawPayments = await _context.Payments
            .Where(p => !p.IsDeleted && p.PaymentStatus == PaymentStatus.Paid && p.DatePaid >= startDate && p.DatePaid <= endDate)
            .ToListAsync();

        return rawPayments
            .GroupBy(p => p.DatePaid.Date)
            .Select(g => new DailyRevenueReportDto
            {
                Date = g.Key,
                TransactionCount = g.Count(),
                GrossAmount = g.Sum(p => p.Amount),
                TotalDiscount = g.Sum(p => p.Discount),
                NetAmount = g.Sum(p => p.FinalAmount)
            })
            .OrderBy(d => d.Date)
            .ToList();
    }

    public async Task<IEnumerable<MonthlyRevenueReportDto>> GetMonthlyRevenueAsync(DateTime startDate, DateTime endDate)
    {
        var rawPayments = await _context.Payments
            .Where(p => !p.IsDeleted && p.PaymentStatus == PaymentStatus.Paid && p.DatePaid >= startDate && p.DatePaid <= endDate)
            .ToListAsync();

        return rawPayments
            .GroupBy(p => $"{p.DatePaid.Year}-{p.DatePaid.Month:D2}")
            .Select(g => new MonthlyRevenueReportDto
            {
                Month = g.Key,
                TransactionCount = g.Count(),
                GrossAmount = g.Sum(p => p.Amount),
                TotalDiscount = g.Sum(p => p.Discount),
                NetAmount = g.Sum(p => p.FinalAmount)
            })
            .OrderBy(m => m.Month)
            .ToList();
    }

    public async Task<IEnumerable<AttendanceReportDto>> GetAttendanceReportAsync(DateTime startDate, DateTime endDate)
    {
        var logs = await _context.AttendanceLogs
            .Include(a => a.Member)
            .Where(a => a.CheckInTime >= startDate && a.CheckInTime <= endDate)
            .OrderBy(a => a.CheckInTime)
            .ToListAsync();

        var memberIds = logs.Select(l => l.MemberID).Distinct().ToList();
        var subscriptions = await _context.Subscriptions
            .Include(s => s.Plan)
            .Where(s => memberIds.Contains(s.MemberID))
            .ToListAsync();

        var report = new List<AttendanceReportDto>();
        foreach (var a in logs)
        {
            var sub = subscriptions.FirstOrDefault(s => s.MemberID == a.MemberID && a.CheckInTime >= s.StartDate && a.CheckInTime <= s.EndDate);
            if (sub == null)
            {
                sub = subscriptions.Where(s => s.MemberID == a.MemberID).OrderByDescending(s => s.EndDate).FirstOrDefault();
            }

            report.Add(new AttendanceReportDto
            {
                AttendanceID = a.AttendanceID,
                MemberName = a.Member != null ? $"{a.Member.FirstName} {a.Member.LastName}" : "Unknown",
                PlanName = sub?.Plan?.PlanName ?? "Walk-in / None",
                CheckInTime = a.CheckInTime,
                CheckOutTime = a.CheckOutTime
            });
        }
        return report;
    }

    public async Task<IEnumerable<MembershipSalesReportDto>> GetMembershipSalesReportAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.Payments
            .Include(p => p.Member)
            .Include(p => p.Subscription)
            .ThenInclude(s => s.Plan)
            .Where(p => !p.IsDeleted && p.PaymentStatus == PaymentStatus.Paid && p.DatePaid >= startDate && p.DatePaid <= endDate)
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
    }

    public async Task<IEnumerable<ExpiringMembershipsReportDto>> GetExpiringMembershipsReportAsync(int nextDays)
    {
        var targetDate = DateTime.UtcNow.AddDays(nextDays);
        return await _context.Subscriptions
            .Include(s => s.Member)
            .Include(s => s.Plan)
            .Where(s => s.Status == "Active" && s.EndDate <= targetDate)
            .OrderBy(s => s.EndDate)
            .Select(s => new ExpiringMembershipsReportDto
            {
                MemberName = s.Member != null ? $"{s.Member.FirstName} {s.Member.LastName}" : "Unknown",
                PlanName = s.Plan != null ? s.Plan.PlanName : "Unknown",
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                Status = s.Status
            })
            .ToListAsync();
    }

    public async Task<IEnumerable<RefundReportDto>> GetRefundReportAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.Payments
            .Include(p => p.Member)
            .Where(p => !p.IsDeleted && p.PaymentStatus == PaymentStatus.Refunded && p.LastModified >= startDate && p.LastModified <= endDate)
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
    }

    public async Task<IEnumerable<CashierActivityReportDto>> GetCashierActivityReportAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.AuditLogs
            .Include(a => a.User)
            .Where(a => a.Timestamp >= startDate && a.Timestamp <= endDate)
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
    }
}
