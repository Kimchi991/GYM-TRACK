using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Exposes operations and financial reports for GymTrackPro.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "Administrator")]
public class ReportsController : ControllerBase
{
    private readonly IReportsService _reportsService;

    public ReportsController(IReportsService reportsService)
    {
        _reportsService = reportsService;
    }

    // --- 1. Daily Revenue ---
    [HttpGet("daily-revenue")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<DailyRevenueReportDto>>), 200)]
    public async Task<IActionResult> GetDailyRevenue([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetDailyRevenueAsync(startDate, endDate);
        return Ok(ApiResponse<IEnumerable<DailyRevenueReportDto>>.SuccessResponse(data));
    }

    [HttpGet("daily-revenue/export")]
    public async Task<IActionResult> ExportDailyRevenue([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetDailyRevenueAsync(startDate, endDate);
        var csv = new StringBuilder();
        csv.AppendLine("Date,TransactionCount,GrossAmount,TotalDiscount,NetAmount");
        foreach (var d in data)
        {
            csv.AppendLine($"{d.Date:yyyy-MM-dd},{d.TransactionCount},{d.GrossAmount:F2},{d.TotalDiscount:F2},{d.NetAmount:F2}");
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"daily_revenue_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv");
    }

    // --- 2. Monthly Revenue ---
    [HttpGet("monthly-revenue")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<MonthlyRevenueReportDto>>), 200)]
    public async Task<IActionResult> GetMonthlyRevenue([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetMonthlyRevenueAsync(startDate, endDate);
        return Ok(ApiResponse<IEnumerable<MonthlyRevenueReportDto>>.SuccessResponse(data));
    }

    [HttpGet("monthly-revenue/export")]
    public async Task<IActionResult> ExportMonthlyRevenue([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetMonthlyRevenueAsync(startDate, endDate);
        var csv = new StringBuilder();
        csv.AppendLine("Month,TransactionCount,GrossAmount,TotalDiscount,NetAmount");
        foreach (var m in data)
        {
            csv.AppendLine($"{m.Month},{m.TransactionCount},{m.GrossAmount:F2},{m.TotalDiscount:F2},{m.NetAmount:F2}");
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"monthly_revenue_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv");
    }

    // --- 3. Attendance ---
    [HttpGet("attendance")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<AttendanceReportDto>>), 200)]
    public async Task<IActionResult> GetAttendance([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetAttendanceReportAsync(startDate, endDate);
        return Ok(ApiResponse<IEnumerable<AttendanceReportDto>>.SuccessResponse(data));
    }

    [HttpGet("attendance/export")]
    public async Task<IActionResult> ExportAttendance([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetAttendanceReportAsync(startDate, endDate);
        var csv = new StringBuilder();
        csv.AppendLine("AttendanceID,MemberName,PlanName,CheckInTime,CheckOutTime");
        foreach (var a in data)
        {
            csv.AppendLine($"{a.AttendanceID},\"{a.MemberName}\",\"{a.PlanName}\",{a.CheckInTime:yyyy-MM-dd HH:mm:ss},{a.CheckOutTime?.ToString("yyyy-MM-dd HH:mm:ss")}");
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"attendance_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv");
    }

    // --- 4. Membership Sales ---
    [HttpGet("membership-sales")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<MembershipSalesReportDto>>), 200)]
    public async Task<IActionResult> GetMembershipSales([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetMembershipSalesReportAsync(startDate, endDate);
        return Ok(ApiResponse<IEnumerable<MembershipSalesReportDto>>.SuccessResponse(data));
    }

    [HttpGet("membership-sales/export")]
    public async Task<IActionResult> ExportMembershipSales([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetMembershipSalesReportAsync(startDate, endDate);
        var csv = new StringBuilder();
        csv.AppendLine("MemberName,PlanName,Amount,Discount,FinalAmount,DatePaid,PaymentMethod");
        foreach (var s in data)
        {
            csv.AppendLine($"\"{s.MemberName}\",\"{s.PlanName}\",{s.Amount:F2},{s.Discount:F2},{s.FinalAmount:F2},{s.DatePaid:yyyy-MM-dd HH:mm:ss},{s.PaymentMethod}");
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"membership_sales_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv");
    }

    // --- 5. Expiring Memberships ---
    [HttpGet("expiring-memberships")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<ExpiringMembershipsReportDto>>), 200)]
    public async Task<IActionResult> GetExpiringMemberships([FromQuery] int nextDays = 7)
    {
        var data = await _reportsService.GetExpiringMembershipsReportAsync(nextDays);
        return Ok(ApiResponse<IEnumerable<ExpiringMembershipsReportDto>>.SuccessResponse(data));
    }

    [HttpGet("expiring-memberships/export")]
    public async Task<IActionResult> ExportExpiringMemberships([FromQuery] int nextDays = 7)
    {
        var data = await _reportsService.GetExpiringMembershipsReportAsync(nextDays);
        var csv = new StringBuilder();
        csv.AppendLine("MemberName,PlanName,StartDate,EndDate,Status");
        foreach (var e in data)
        {
            csv.AppendLine($"\"{e.MemberName}\",\"{e.PlanName}\",{e.StartDate:yyyy-MM-dd},{e.EndDate:yyyy-MM-dd},{e.Status}");
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"expiring_memberships_next_{nextDays}_days.csv");
    }

    // --- 6. Refunds ---
    [HttpGet("refunds")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<RefundReportDto>>), 200)]
    public async Task<IActionResult> GetRefunds([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetRefundReportAsync(startDate, endDate);
        return Ok(ApiResponse<IEnumerable<RefundReportDto>>.SuccessResponse(data));
    }

    [HttpGet("refunds/export")]
    public async Task<IActionResult> ExportRefunds([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetRefundReportAsync(startDate, endDate);
        var csv = new StringBuilder();
        csv.AppendLine("PaymentID,MemberName,ReceiptNumber,RefundedAmount,DateRefunded");
        foreach (var r in data)
        {
            csv.AppendLine($"{r.PaymentID},\"{r.MemberName}\",{r.ReceiptNumber},{r.Amount:F2},{r.DateRefunded:yyyy-MM-dd HH:mm:ss}");
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"refunds_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv");
    }

    // --- 7. Cashier Activity ---
    [HttpGet("cashier-activity")]
    [ProducesResponseType(typeof(ApiResponse<IEnumerable<CashierActivityReportDto>>), 200)]
    public async Task<IActionResult> GetCashierActivity([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetCashierActivityReportAsync(startDate, endDate);
        return Ok(ApiResponse<IEnumerable<CashierActivityReportDto>>.SuccessResponse(data));
    }

    [HttpGet("cashier-activity/export")]
    public async Task<IActionResult> ExportCashierActivity([FromQuery] DateTime startDate, [FromQuery] DateTime endDate)
    {
        var data = await _reportsService.GetCashierActivityReportAsync(startDate, endDate);
        var csv = new StringBuilder();
        csv.AppendLine("Username,Action,Details,Timestamp,IpAddress");
        foreach (var c in data)
        {
            csv.AppendLine($"\"{c.Username}\",\"{c.Action}\",\"{c.Details.Replace("\"", "\"\"")}\",{c.Timestamp:yyyy-MM-dd HH:mm:ss},{c.IpAddress}");
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"cashier_activity_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv");
    }
}
