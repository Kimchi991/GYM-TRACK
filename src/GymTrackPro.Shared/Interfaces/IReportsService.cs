using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Shared.Interfaces;

public interface IReportsService
{
    Task<IEnumerable<DailyRevenueReportDto>> GetDailyRevenueAsync(DateTime startDate, DateTime endDate);
    Task<IEnumerable<MonthlyRevenueReportDto>> GetMonthlyRevenueAsync(DateTime startDate, DateTime endDate);
    Task<IEnumerable<AttendanceReportDto>> GetAttendanceReportAsync(DateTime startDate, DateTime endDate);
    Task<IEnumerable<MembershipSalesReportDto>> GetMembershipSalesReportAsync(DateTime startDate, DateTime endDate);
    Task<IEnumerable<ExpiringMembershipsReportDto>> GetExpiringMembershipsReportAsync(int nextDays);
    Task<IEnumerable<RefundReportDto>> GetRefundReportAsync(DateTime startDate, DateTime endDate);
    Task<IEnumerable<CashierActivityReportDto>> GetCashierActivityReportAsync(DateTime startDate, DateTime endDate);
}
