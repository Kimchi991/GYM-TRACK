using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Mobile.Services;

public interface IApiService
{
    // Authentication
    Task<ApiResponse<UserResponseDto>> SyncUserWithBackendAsync(string firebaseToken);
    Task<ApiResponse<UserResponseDto>> ActivateInviteAsync(ActivateInviteDto dto);
    Task<ApiResponse<UserResponseDto>> GetCurrentUserAsync();
    void SetAuthToken(string token);
    string? GetAuthToken();
    void ClearAuthToken();
    Task InitializeTokenAsync();

    // Dashboard
    Task<ApiResponse<DashboardMetricsDto>> GetDashboardMetricsAsync();

    // Members
    Task<ApiResponse<PagedResultDto<MemberResponseDto>>> GetMembersAsync(string search = "", int page = 1, int pageSize = 10);
    Task<ApiResponse<MemberResponseDto>> GetMemberByIdAsync(int id);
    Task<ApiResponse<MemberResponseDto>> CreateMemberAsync(CreateMemberDto memberDto);
    Task<ApiResponse<MemberResponseDto>> UpdateMemberAsync(int id, UpdateMemberDto memberDto);
    Task<ApiResponse> DeleteMemberAsync(int id);

    // Invites
    Task<ApiResponse<AppInviteCodeResponseDto>> GenerateMemberInviteAsync(
        int memberId,
        CreateAppInviteDto request);
    Task<ApiResponse<AppInviteResponseDto>> GetMemberInviteStatusAsync(int memberId);
    Task<ApiResponse> RevokeMemberInviteAsync(int memberId);

    // Attendance
    Task<ApiResponse<AttendanceDto>> CheckInAsync(string qrCode);
    Task<ApiResponse> CheckOutAsync(int attendanceId);
    Task<ApiResponse<IEnumerable<AttendanceDto>>> GetAttendanceByMemberIdAsync(int memberId);

    // Gym Goer Self-Service
    Task<ApiResponse<GoerDashboardDto>> GetGoerDashboardAsync();
    Task<ApiResponse<GoerDigitalCardDto>> GetGoerDigitalCardAsync();
    Task<ApiResponse<AttendanceDto>> GetGoerCurrentAttendanceAsync();
    Task<ApiResponse<PagedResultDto<AttendanceDto>>> GetGoerAttendanceHistoryAsync(DateTime? from, DateTime? to, int page = 1, int pageSize = 10);
    Task<ApiResponse<AttendanceDto>> GoerCheckOutAsync(Guid operationId);
    Task<ApiResponse<GoerProgressDto>> GetGoerProgressAsync(string month);

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
    Task<ApiResponse<IEnumerable<MonthlyRevenueReportDto>>> GetMonthlyRevenueReportAsync(DateTime startDate, DateTime endDate);
    Task<ApiResponse<IEnumerable<AttendanceReportDto>>> GetAttendanceReportAsync(DateTime startDate, DateTime endDate);
    Task<ApiResponse<IEnumerable<MembershipSalesReportDto>>> GetMembershipSalesReportAsync(DateTime startDate, DateTime endDate);
    Task<ApiResponse<IEnumerable<RefundReportDto>>> GetRefundReportAsync(DateTime startDate, DateTime endDate);
    Task<ApiResponse<IEnumerable<ExpiringMembershipsReportDto>>> GetExpiringMembershipsReportAsync(int nextDays = 7);
    Task<byte[]> ExportDailyRevenueCsvAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> ExportMonthlyRevenueCsvAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> ExportAttendanceCsvAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> ExportMembershipSalesCsvAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> ExportRefundsCsvAsync(DateTime startDate, DateTime endDate);
    Task<byte[]> ExportExpiringMembershipsCsvAsync(int nextDays = 7);

    // Owner Analytics
    Task<ApiResponse<OwnerAttendanceSummaryDto>> GetOwnerAttendanceSummaryAsync(DateTime? from, DateTime? to, string bucket = "day");
    Task<byte[]> ExportOwnerAttendanceSummaryCsvAsync(DateTime? from, DateTime? to, string bucket = "day");
}
