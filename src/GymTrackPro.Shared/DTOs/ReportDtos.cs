using System;

namespace GymTrackPro.Shared.DTOs;

public class DailyRevenueReportDto
{
    public DateTime Date { get; set; }
    public int TransactionCount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal NetAmount { get; set; }
}

public class MonthlyRevenueReportDto
{
    public string Month { get; set; } = string.Empty; // e.g. "2026-07"
    public int TransactionCount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal NetAmount { get; set; }
}

public class AttendanceReportDto
{
    public int AttendanceID { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
}

public class MembershipSalesReportDto
{
    public string MemberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Discount { get; set; }
    public decimal FinalAmount { get; set; }
    public DateTime DatePaid { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}

public class ExpiringMembershipsReportDto
{
    public string MemberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class RefundReportDto
{
    public int PaymentID { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string ReceiptNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime DateRefunded { get; set; }
}

public class CashierActivityReportDto
{
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string IpAddress { get; set; } = string.Empty;
}
