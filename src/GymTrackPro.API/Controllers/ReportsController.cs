using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;
using GymTrackPro.API.Authorization;

namespace GymTrackPro.API.Controllers;

/// <summary>
/// Exposes operations and financial reports for GymTrackPro.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Policy = Policies.OwnerOnly)]
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
            AppendCsvRow(csv,
                d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                d.TransactionCount,
                d.GrossAmount.ToString("F2", CultureInfo.InvariantCulture),
                d.TotalDiscount.ToString("F2", CultureInfo.InvariantCulture),
                d.NetAmount.ToString("F2", CultureInfo.InvariantCulture));
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
            AppendCsvRow(csv,
                m.Month,
                m.TransactionCount,
                m.GrossAmount.ToString("F2", CultureInfo.InvariantCulture),
                m.TotalDiscount.ToString("F2", CultureInfo.InvariantCulture),
                m.NetAmount.ToString("F2", CultureInfo.InvariantCulture));
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
        csv.AppendLine("AttendanceID,MemberName,PlanName,CheckInTimeUtc,CheckOutTimeUtc");
        foreach (var a in data)
        {
            AppendCsvRow(csv,
                a.AttendanceID,
                a.MemberName,
                a.PlanName,
                FormatUtcInstant(a.CheckInTime),
                a.CheckOutTime.HasValue ? FormatUtcInstant(a.CheckOutTime.Value) : null);
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
        csv.AppendLine("MemberName,PlanName,Amount,Discount,FinalAmount,DatePaidUtc,PaymentMethod");
        foreach (var s in data)
        {
            AppendCsvRow(csv,
                s.MemberName,
                s.PlanName,
                s.Amount.ToString("F2", CultureInfo.InvariantCulture),
                s.Discount.ToString("F2", CultureInfo.InvariantCulture),
                s.FinalAmount.ToString("F2", CultureInfo.InvariantCulture),
                FormatUtcInstant(s.DatePaid),
                s.PaymentMethod);
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
            AppendCsvRow(csv,
                e.MemberName,
                e.PlanName,
                e.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                e.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                e.Status);
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
        csv.AppendLine("PaymentID,MemberName,ReceiptNumber,RefundedAmount,DateRefundedUtc");
        foreach (var r in data)
        {
            AppendCsvRow(csv,
                r.PaymentID,
                r.MemberName,
                r.ReceiptNumber,
                r.Amount.ToString("F2", CultureInfo.InvariantCulture),
                FormatUtcInstant(r.DateRefunded));
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
        csv.AppendLine("Username,Action,Details,TimestampUtc,IpAddress");
        foreach (var c in data)
        {
            AppendCsvRow(csv,
                c.Username,
                c.Action,
                c.Details,
                FormatUtcInstant(c.Timestamp),
                c.IpAddress);
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"cashier_activity_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv");
    }
    // --- 8. Attendance Summary (Graph) ---
    [HttpGet("attendance/summary")]
    [ProducesResponseType(typeof(ApiResponse<OwnerAttendanceSummaryDto>), 200)]
    public async Task<IActionResult> GetAttendanceSummary(
        [FromQuery(Name = "from")] DateOnly? from,
        [FromQuery(Name = "to")] DateOnly? to,
        [FromQuery(Name = "bucket")] string bucket = "day")
    {
        var data = await _reportsService.GetAttendanceSummaryAsync(from, to, bucket);
        return Ok(ApiResponse<OwnerAttendanceSummaryDto>.SuccessResponse(data));
    }

    [HttpGet("attendance/summary/export")]
    public async Task<IActionResult> ExportAttendanceSummary(
        [FromQuery(Name = "from")] DateOnly? from,
        [FromQuery(Name = "to")] DateOnly? to,
        [FromQuery(Name = "bucket")] string bucket = "day")
    {
        var data = await _reportsService.GetAttendanceSummaryAsync(from, to, bucket);
        var csv = new StringBuilder();
        csv.AppendLine("GymDate,Label,VisitCount");
        foreach (var point in data.Points)
        {
            AppendCsvRow(
                csv,
                point.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                point.Label,
                point.VisitCount);
        }

        return File(
            Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv",
            $"attendance_summary_{data.FromGymDate:yyyyMMdd}_{data.EndExclusiveGymDate:yyyyMMdd}.csv");
    }

    private static string FormatUtcInstant(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => throw new InvalidOperationException("A persisted UTC timestamp had an invalid DateTime kind.")
        };
        return utc.ToString("O", CultureInfo.InvariantCulture);
    }

    private static void AppendCsvRow(StringBuilder builder, params object?[] cells)
    {
        builder.AppendLine(string.Join(',', cells.Select(CsvCellEncoder.Encode)));
    }
}

public static class CsvCellEncoder
{
    public static string Encode(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var firstContentIndex = 0;
        while (firstContentIndex < text.Length && char.IsWhiteSpace(text[firstContentIndex]))
        {
            firstContentIndex++;
        }

        if (firstContentIndex < text.Length
            && text[firstContentIndex] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
