using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Mobile.Services;

public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private string? _authToken;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ApiService()
    {
#if ANDROID
        var baseUri = "http://10.0.2.2:5221/api/v1/";
#else
        var baseUri = "http://localhost:5221/api/v1/";
#endif

        // Configure HttpClient
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUri) };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Load token on initialization
        InitializeToken();
    }

    private void InitializeToken()
    {
        try
        {
            var tokenTask = SecureStorage.Default.GetAsync("auth_token");
            tokenTask.Wait();
            var token = tokenTask.Result;
            if (!string.IsNullOrEmpty(token))
            {
                SetAuthToken(token);
            }
        }
        catch
        {
            // SecureStorage might fail if run in non-UI thread or emulator environment initialization
        }
    }

    public void SetAuthToken(string token)
    {
        _authToken = token;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public string? GetAuthToken()
    {
        return _authToken;
    }

    public void ClearAuthToken()
    {
        _authToken = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        SecureStorage.Default.Remove("auth_token");
    }

    private async Task<ApiResponse<T>> HandleResponseAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(_jsonOptions);
            return result ?? new ApiResponse<T> { Success = false, Message = "Failed to deserialize response." };
        }

        try
        {
            var errorResult = await response.Content.ReadFromJsonAsync<ApiResponse>(_jsonOptions);
            return new ApiResponse<T>
            {
                Success = false,
                Message = errorResult?.Message ?? $"Request failed with status code {response.StatusCode}."
            };
        }
        catch
        {
            return new ApiResponse<T> { Success = false, Message = $"Server returned error code {response.StatusCode}." };
        }
    }

    private async Task<ApiResponse> HandleResponseAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<ApiResponse>(_jsonOptions);
            return result ?? new ApiResponse { Success = true };
        }

        try
        {
            var errorResult = await response.Content.ReadFromJsonAsync<ApiResponse>(_jsonOptions);
            return errorResult ?? new ApiResponse { Success = false, Message = $"Request failed with status code {response.StatusCode}." };
        }
        catch
        {
            return new ApiResponse { Success = false, Message = $"Server returned error code {response.StatusCode}." };
        }
    }

    // --- Authentication ---

    public async Task<ApiResponse<UserResponseDto>> LoginAsync(string username, string password)
    {
        var loginDto = new LoginDto { Username = username, Password = password };
        var response = await _httpClient.PostAsJsonAsync("auth/login", loginDto);
        var result = await HandleResponseAsync<UserResponseDto>(response);
        if (result.Success && result.Data != null)
        {
            SetAuthToken(result.Data.Token);
            await SecureStorage.Default.SetAsync("auth_token", result.Data.Token);
        }
        return result;
    }

    public async Task<ApiResponse<UserResponseDto>> RegisterAsync(RegisterUserDto registerDto)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/register", registerDto);
        return await HandleResponseAsync<UserResponseDto>(response);
    }

    public async Task<ApiResponse> ForgotPasswordAsync(string email)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/forgot-password", email);
        return await HandleResponseAsync(response);
    }

    public async Task<ApiResponse> ResetPasswordAsync(ResetPasswordDto resetDto)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/reset-password", resetDto);
        return await HandleResponseAsync(response);
    }

    public async Task<ApiResponse> VerifyEmailAsync(string email, string token)
    {
        var verifyDto = new VerifyEmailDto { Email = email, Token = token };
        var response = await _httpClient.PostAsJsonAsync("auth/verify-email", verifyDto);
        return await HandleResponseAsync(response);
    }

    // --- Dashboard ---

    public async Task<ApiResponse<DashboardMetricsDto>> GetDashboardMetricsAsync()
    {
        var response = await _httpClient.GetAsync("dashboard/metrics");
        return await HandleResponseAsync<DashboardMetricsDto>(response);
    }

    // --- Members ---

    public async Task<ApiResponse<PagedResultDto<MemberResponseDto>>> GetMembersAsync(string search = "", int page = 1, int pageSize = 10)
    {
        var response = await _httpClient.GetAsync($"members/search?search={Uri.EscapeDataString(search)}&page={page}&pageSize={pageSize}");
        return await HandleResponseAsync<PagedResultDto<MemberResponseDto>>(response);
    }

    public async Task<ApiResponse<MemberResponseDto>> GetMemberByIdAsync(int id)
    {
        var response = await _httpClient.GetAsync($"members/{id}");
        return await HandleResponseAsync<MemberResponseDto>(response);
    }

    public async Task<ApiResponse<MemberResponseDto>> CreateMemberAsync(CreateMemberDto memberDto)
    {
        var response = await _httpClient.PostAsJsonAsync("members", memberDto);
        return await HandleResponseAsync<MemberResponseDto>(response);
    }

    public async Task<ApiResponse<MemberResponseDto>> UpdateMemberAsync(int id, UpdateMemberDto memberDto)
    {
        var response = await _httpClient.PutAsJsonAsync($"members/{id}", memberDto);
        return await HandleResponseAsync<MemberResponseDto>(response);
    }

    public async Task<ApiResponse> DeleteMemberAsync(int id)
    {
        var response = await _httpClient.DeleteAsync($"members/{id}");
        return await HandleResponseAsync(response);
    }

    // --- Attendance ---

    public async Task<ApiResponse<AttendanceDto>> CheckInAsync(string qrCode)
    {
        var response = await _httpClient.PostAsJsonAsync("attendance/checkin", qrCode);
        return await HandleResponseAsync<AttendanceDto>(response);
    }

    public async Task<ApiResponse> CheckOutAsync(int attendanceId)
    {
        var response = await _httpClient.PostAsync($"attendance/{attendanceId}/checkout", null);
        return await HandleResponseAsync(response);
    }

    public async Task<ApiResponse<IEnumerable<AttendanceDto>>> GetAttendanceByMemberIdAsync(int memberId)
    {
        var response = await _httpClient.GetAsync($"attendance/member/{memberId}");
        return await HandleResponseAsync<IEnumerable<AttendanceDto>>(response);
    }

    // --- Payments ---

    public async Task<ApiResponse<PaymentResponseDto>> ProcessPaymentAsync(CreatePaymentDto paymentDto)
    {
        var response = await _httpClient.PostAsJsonAsync("payments", paymentDto);
        return await HandleResponseAsync<PaymentResponseDto>(response);
    }

    public async Task<ApiResponse<PaymentResponseDto>> RefundPaymentAsync(int id)
    {
        var response = await _httpClient.PostAsync($"payments/{id}/refund", null);
        return await HandleResponseAsync<PaymentResponseDto>(response);
    }

    public async Task<ApiResponse<IEnumerable<PaymentResponseDto>>> GetPaymentsAsync(int memberId = 0)
    {
        HttpResponseMessage response;
        if (memberId > 0)
        {
            response = await _httpClient.GetAsync($"payments/member/{memberId}");
        }
        else
        {
            response = await _httpClient.GetAsync("payments/search");
        }
        return await HandleResponseAsync<IEnumerable<PaymentResponseDto>>(response);
    }

    // --- Plans ---

    public async Task<ApiResponse<IEnumerable<MembershipPlanResponseDto>>> GetPlansAsync()
    {
        var response = await _httpClient.GetAsync("plans");
        return await HandleResponseAsync<IEnumerable<MembershipPlanResponseDto>>(response);
    }

    public async Task<ApiResponse<MembershipPlanResponseDto>> CreatePlanAsync(CreateMembershipPlanDto planDto)
    {
        var response = await _httpClient.PostAsJsonAsync("plans", planDto);
        return await HandleResponseAsync<MembershipPlanResponseDto>(response);
    }

    public async Task<ApiResponse<MembershipPlanResponseDto>> UpdatePlanAsync(int id, CreateMembershipPlanDto planDto)
    {
        var response = await _httpClient.PutAsJsonAsync($"plans/{id}", planDto);
        return await HandleResponseAsync<MembershipPlanResponseDto>(response);
    }

    public async Task<ApiResponse> DeletePlanAsync(int id)
    {
        var response = await _httpClient.DeleteAsync($"plans/{id}");
        return await HandleResponseAsync(response);
    }

    // --- Subscriptions ---

    public async Task<ApiResponse<SubscriptionResponseDto>> CreateSubscriptionAsync(CreateSubscriptionDto subDto)
    {
        var response = await _httpClient.PostAsJsonAsync("subscriptions", subDto);
        return await HandleResponseAsync<SubscriptionResponseDto>(response);
    }

    public async Task<ApiResponse> PauseSubscriptionAsync(int id, PauseSubscriptionDto pauseDto)
    {
        var response = await _httpClient.PostAsJsonAsync($"subscriptions/{id}/pause", pauseDto);
        return await HandleResponseAsync(response);
    }

    public async Task<ApiResponse> ResumeSubscriptionAsync(int id)
    {
        var response = await _httpClient.PostAsync($"subscriptions/{id}/resume", null);
        return await HandleResponseAsync(response);
    }

    // --- Settings ---

    public async Task<ApiResponse<IEnumerable<SystemSettingDto>>> GetSettingsAsync()
    {
        var response = await _httpClient.GetAsync("settings");
        return await HandleResponseAsync<IEnumerable<SystemSettingDto>>(response);
    }

    public async Task<ApiResponse> UpdateSettingAsync(string key, string value)
    {
        var response = await _httpClient.PutAsJsonAsync($"settings/{key}", value);
        return await HandleResponseAsync(response);
    }

    // --- Notifications ---

    public async Task<ApiResponse<IEnumerable<Notification>>> GetNotificationsAsync(int? memberId = null)
    {
        var url = memberId.HasValue ? $"notifications?memberId={memberId.Value}" : "notifications";
        var response = await _httpClient.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<List<Notification>>(_jsonOptions);
            return new ApiResponse<IEnumerable<Notification>>
            {
                Success = true,
                Data = result,
                Message = "Notifications retrieved successfully."
            };
        }
        return new ApiResponse<IEnumerable<Notification>> { Success = false, Message = "Failed to fetch notifications." };
    }

    public async Task<ApiResponse> MarkNotificationAsReadAsync(int id)
    {
        var response = await _httpClient.PutAsync($"notifications/{id}/read", null);
        if (response.IsSuccessStatusCode)
        {
            return new ApiResponse { Success = true, Message = "Notification marked as read." };
        }
        return new ApiResponse { Success = false, Message = "Failed to mark notification as read." };
    }

    // --- Reports ---

    public async Task<ApiResponse<IEnumerable<DailyRevenueReportDto>>> GetDailyRevenueReportAsync(DateTime startDate, DateTime endDate)
    {
        var response = await _httpClient.GetAsync($"reports/daily-revenue?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        return await HandleResponseAsync<IEnumerable<DailyRevenueReportDto>>(response);
    }

    public async Task<ApiResponse<IEnumerable<AttendanceReportDto>>> GetAttendanceReportAsync(DateTime startDate, DateTime endDate)
    {
        var response = await _httpClient.GetAsync($"reports/attendance?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        return await HandleResponseAsync<IEnumerable<AttendanceReportDto>>(response);
    }

    public async Task<ApiResponse<IEnumerable<MembershipSalesReportDto>>> GetMembershipSalesReportAsync(DateTime startDate, DateTime endDate)
    {
        var response = await _httpClient.GetAsync($"reports/membership-sales?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        return await HandleResponseAsync<IEnumerable<MembershipSalesReportDto>>(response);
    }

    public async Task<ApiResponse<IEnumerable<RefundReportDto>>> GetRefundReportAsync(DateTime startDate, DateTime endDate)
    {
        var response = await _httpClient.GetAsync($"reports/refunds?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        return await HandleResponseAsync<IEnumerable<RefundReportDto>>(response);
    }

    public async Task<ApiResponse<IEnumerable<ExpiringMembershipsReportDto>>> GetExpiringMembershipsReportAsync(int nextDays = 7)
    {
        var response = await _httpClient.GetAsync($"reports/expiring-memberships?nextDays={nextDays}");
        return await HandleResponseAsync<IEnumerable<ExpiringMembershipsReportDto>>(response);
    }

    public async Task<byte[]> ExportDailyRevenueCsvAsync(DateTime startDate, DateTime endDate)
    {
        var response = await _httpClient.GetAsync($"reports/daily-revenue/export?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : Array.Empty<byte>();
    }

    public async Task<byte[]> ExportAttendanceCsvAsync(DateTime startDate, DateTime endDate)
    {
        var response = await _httpClient.GetAsync($"reports/attendance/export?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : Array.Empty<byte>();
    }

    public async Task<byte[]> ExportMembershipSalesCsvAsync(DateTime startDate, DateTime endDate)
    {
        var response = await _httpClient.GetAsync($"reports/membership-sales/export?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : Array.Empty<byte>();
    }

    public async Task<byte[]> ExportRefundsCsvAsync(DateTime startDate, DateTime endDate)
    {
        var response = await _httpClient.GetAsync($"reports/refunds/export?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : Array.Empty<byte>();
    }
}
