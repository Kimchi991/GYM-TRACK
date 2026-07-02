using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Mobile.Services;

public interface IApiService
{
    // Authentication
    Task<ApiResponse<UserResponseDto>> LoginAsync(string username, string password);
    Task<ApiResponse<UserResponseDto>> RegisterAsync(RegisterUserDto registerDto);
    Task<ApiResponse> ForgotPasswordAsync(string email);
    Task<ApiResponse> ResetPasswordAsync(ResetPasswordDto resetDto);
    Task<ApiResponse> VerifyEmailAsync(string email, string token);
    void SetAuthToken(string token);
    string? GetAuthToken();
    void ClearAuthToken();

    // Dashboard
    Task<ApiResponse<DashboardMetricsDto>> GetDashboardMetricsAsync();

    // Members
    Task<ApiResponse<PagedResultDto<MemberResponseDto>>> GetMembersAsync(string search = "", int page = 1, int pageSize = 10);
    Task<ApiResponse<MemberResponseDto>> GetMemberByIdAsync(int id);
    Task<ApiResponse<MemberResponseDto>> CreateMemberAsync(CreateMemberDto memberDto);
    Task<ApiResponse<MemberResponseDto>> UpdateMemberAsync(int id, UpdateMemberDto memberDto);
    Task<ApiResponse> DeleteMemberAsync(int id);

    // Attendance
    Task<ApiResponse<AttendanceDto>> CheckInAsync(string qrCode);
    Task<ApiResponse> CheckOutAsync(int attendanceId);
    Task<ApiResponse<IEnumerable<AttendanceDto>>> GetAttendanceByMemberIdAsync(int memberId);

    // Payments
    Task<ApiResponse<PaymentResponseDto>> ProcessPaymentAsync(CreatePaymentDto paymentDto);
    Task<ApiResponse<PaymentResponseDto>> RefundPaymentAsync(int id);
    Task<ApiResponse<IEnumerable<PaymentResponseDto>>> GetPaymentsAsync(int memberId = 0);

    // Plans
    Task<ApiResponse<IEnumerable<MembershipPlanResponseDto>>> GetPlansAsync();
    Task<ApiResponse<MembershipPlanResponseDto>> CreatePlanAsync(CreateMembershipPlanDto planDto);
    Task<ApiResponse<MembershipPlanResponseDto>> UpdatePlanAsync(int id, CreateMembershipPlanDto planDto);
    Task<ApiResponse> DeletePlanAsync(int id);

    // Subscriptions
    Task<ApiResponse<SubscriptionResponseDto>> CreateSubscriptionAsync(CreateSubscriptionDto subDto);
    Task<ApiResponse> PauseSubscriptionAsync(int id, PauseSubscriptionDto pauseDto);
    Task<ApiResponse> ResumeSubscriptionAsync(int id);
    Task<ApiResponse<IEnumerable<SubscriptionResponseDto>>> GetSubscriptionsByMemberIdAsync(int memberId);

    // Settings
    Task<ApiResponse<IEnumerable<SystemSettingDto>>> GetSettingsAsync();
    Task<ApiResponse> UpdateSettingAsync(string key, string value);

    // Notifications
    Task<ApiResponse<IEnumerable<Notification>>> GetNotificationsAsync(int? memberId = null);
    Task<ApiResponse> MarkNotificationAsReadAsync(int id);

    // Reports
    Task<ApiResponse<IEnumerable<DailyRevenueReportDto>>> GetDailyRevenueReportAsync(DateTime startDate, DateTime endDate);
    Task<ApiResponse<IEnumerable<AttendanceReportDto>>> GetAttendanceReportAsync(DateTime startDate, DateTime endDate);
    Task<ApiResponse<IEnumerable<MembershipSalesReportDto>>> GetMembershipSalesReportAsync(DateTime startDate, DateTime endDate);
    Task<ApiResponse<IEnumerable<RefundReportDto>>> GetRefundReportAsync(DateTime startDate, DateTime endDate);
    Task<ApiResponse<IEnumerable<ExpiringMembershipsReportDto>>> GetExpiringMembershipsReportAsync(int nextDays = 7);
    Task<byte[]> ExportDailyRevenueCsvAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> ExportAttendanceCsvAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> ExportMembershipSalesCsvAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> ExportRefundsCsvAsync(DateTime startDate, DateTime endDate);
}
