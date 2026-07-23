using System;
using System.Collections.Generic;

namespace GymTrackPro.Shared.DTOs;

public class DashboardMetricsDto
{
    public int MembersCheckedInCount { get; set; }
    public string MembersCheckedInLabel { get; set; } = "Open sessions";
    public int VisitsTodayCount { get; set; }
    public int StaleOpenSessionCount { get; set; }
    public int StaleSessionThresholdHours { get; set; }
    public int ActiveMembershipsCount { get; set; }
    public decimal RevenueToday { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public int ExpiringMembershipsCount { get; set; }
    public int NewRegistrationsCount { get; set; }
    public int PendingApplicationsCount { get; set; }
    public List<HourlyCheckInDto> CheckInsByHour { get; set; } = new();
    public List<PlanRevenueDto> RevenueByPlan { get; set; } = new();
    public List<LiveOccupancyDto> CurrentlyCheckedIn { get; set; } = new();
    public List<PlanMembershipCountDto> MembershipPlanDistribution { get; set; } = new();
}

public class LiveOccupancyDto
{
    public string MemberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime CheckInTime { get; set; }
    public string Status { get; set; } = "Active";
}

public class PlanMembershipCountDto
{
    public string PlanName { get; set; } = string.Empty;
    public int ActiveMembersCount { get; set; }
}

public class HourlyCheckInDto
{
    public int Hour { get; set; }
    public int Count { get; set; }
}

public class PlanRevenueDto
{
    public string PlanName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
}
