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

public class DashboardService : IDashboardService
{
    private readonly GymDbContext _context;

    public DashboardService(GymDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardMetricsDto> GetDashboardMetricsAsync()
    {
        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);
        var sevenDaysAgo = now.AddDays(-7);
        var sevenDaysFuture = now.AddDays(7);

        // 1. Members currently checked in
        int checkedInCount = await _context.AttendanceLogs
            .CountAsync(a => a.CheckOutTime == null);

        // 2. Active memberships
        int activeMembershipsCount = await _context.Subscriptions
            .CountAsync(s => s.Status == "Active");

        // 3. Revenue today
        decimal revenueToday = await _context.Payments
            .Where(p => !p.IsDeleted && p.PaymentStatus == PaymentStatus.Paid && p.DatePaid >= todayStart)
            .SumAsync(p => (decimal?)p.FinalAmount) ?? 0.00m;

        // 4. Revenue this month
        decimal revenueThisMonth = await _context.Payments
            .Where(p => !p.IsDeleted && p.PaymentStatus == PaymentStatus.Paid && p.DatePaid >= startOfMonth)
            .SumAsync(p => (decimal?)p.FinalAmount) ?? 0.00m;

        // 5. Expiring memberships (Active, expiring in next 7 days)
        int expiringCount = await _context.Subscriptions
            .CountAsync(s => s.Status == "Active" && s.EndDate >= now && s.EndDate <= sevenDaysFuture);

        // 6. New member registrations (last 7 days)
        int newRegistrationsCount = await _context.Members
            .CountAsync(m => !m.IsDeleted && m.DateRegistered >= sevenDaysAgo);

        // 7. Check-ins by hour (for today)
        var checkinsToday = await _context.AttendanceLogs
            .Where(a => a.CheckInTime >= todayStart)
            .ToListAsync();

        var hourlyCheckins = checkinsToday
            .GroupBy(a => a.CheckInTime.Hour)
            .Select(g => new HourlyCheckInDto
            {
                Hour = g.Key,
                Count = g.Count()
            })
            .OrderBy(h => h.Hour)
            .ToList();

        // 8. Revenue by plan
        var revenueByPlanRaw = await _context.Payments
            .Include(p => p.Subscription)
            .ThenInclude(s => s.Plan)
            .Where(p => !p.IsDeleted && p.PaymentStatus == PaymentStatus.Paid)
            .ToListAsync();

        var revenueByPlanList = revenueByPlanRaw
            .GroupBy(p => p.Subscription?.Plan?.PlanName ?? "Unknown Plan")
            .Select(g => new PlanRevenueDto
            {
                PlanName = g.Key,
                Revenue = g.Sum(p => p.FinalAmount)
            })
            .OrderByDescending(r => r.Revenue)
            .ToList();

        return new DashboardMetricsDto
        {
            MembersCheckedInCount = checkedInCount,
            ActiveMembershipsCount = activeMembershipsCount,
            RevenueToday = revenueToday,
            RevenueThisMonth = revenueThisMonth,
            ExpiringMembershipsCount = expiringCount,
            NewRegistrationsCount = newRegistrationsCount,
            CheckInsByHour = hourlyCheckins,
            RevenueByPlan = revenueByPlanList
        };
    }
}
