using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Entities;

namespace GymTrackPro.Mobile.Services;

public class ApiService : IApiService
{
    private const int MaximumProfilePictureBytes = 10 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ApiService(IFirebaseAuthService authService)
        : this(CreateAuthenticatedClient(authService))
    {
    }

    internal ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (!_httpClient.DefaultRequestHeaders.Accept.Any(value =>
                value.MediaType == "application/json"))
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    private static HttpClient CreateAuthenticatedClient(IFirebaseAuthService authService)
    {
        var endpoint = ApiEndpointConfiguration.LoadForCurrentBuild();
        var handler = new AuthenticatedHttpClientHandler(authService, endpoint);
        return new HttpClient(handler) { BaseAddress = endpoint.BaseUri };
    }

    public Task InitializeTokenAsync()
    {
        return Task.CompletedTask;
    }

    public void SetAuthToken(string token)
    {
        // Handled by AuthenticatedHttpClientHandler
    }

    public string? GetAuthToken()
    {
        return null; // Token is resolved dynamically by the handler
    }

    public void ClearAuthToken()
    {
        // Handled by IFirebaseAuthService / AuthenticatedHttpClientHandler
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
                Message = errorResult?.Message ?? $"Request failed with status code {response.StatusCode}.",
                ErrorCode = errorResult?.ErrorCode,
                Errors = errorResult?.Errors ?? new List<string>()
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

    public async Task<ApiResponse<UserResponseDto>> SyncUserWithBackendAsync(string firebaseToken)
    {
        var response = await _httpClient.PostAsync("auth/sync-user", null);
        return await HandleResponseAsync<UserResponseDto>(response);
    }

    public async Task<ApiResponse<UserResponseDto>> ActivateInviteAsync(ActivateInviteDto dto, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("auth/activate", dto, cancellationToken);
        return await HandleResponseAsync<UserResponseDto>(response);
    }

    public async Task<ApiResponse<UserResponseDto>> GetCurrentUserAsync()
    {
        var response = await _httpClient.GetAsync("me");
        return await HandleResponseAsync<UserResponseDto>(response);
    }

    public async Task<StartupIdentityLookupResult> GetCurrentUserForStartupAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync("me", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var statusCode = response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var result = await response.Content
                        .ReadFromJsonAsync<ApiResponse<UserResponseDto>>(
                            _jsonOptions,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return result?.Success == true && result.Data is not null
                        ? new StartupIdentityLookupResult(
                            StartupIdentityLookupStatus.Success,
                            result.Data,
                            statusCode)
                        : new StartupIdentityLookupResult(
                            StartupIdentityLookupStatus.Rejected,
                            HttpStatusCode: statusCode,
                            ErrorCode: result?.ErrorCode);
                }
                catch (JsonException)
                {
                    // A malformed success response cannot prove an active app identity.
                    return new StartupIdentityLookupResult(
                        StartupIdentityLookupStatus.Rejected,
                        HttpStatusCode: statusCode,
                        ErrorCode: "INVALID_IDENTITY_RESPONSE");
                }
            }

            if (statusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout)
            {
                return new StartupIdentityLookupResult(
                    StartupIdentityLookupStatus.Unavailable,
                    HttpStatusCode: statusCode);
            }

            string? errorCode = null;
            try
            {
                var error = await response.Content
                    .ReadFromJsonAsync<ApiResponse>(_jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                errorCode = error?.ErrorCode;
            }
            catch (JsonException)
            {
                // HTTP authorization/business status remains authoritative.
            }

            // 401/403 and every non-transient business response fail closed. This
            // includes inactive, deleted, or unlinked SQL application identities.
            return new StartupIdentityLookupResult(
                StartupIdentityLookupStatus.Rejected,
                HttpStatusCode: statusCode,
                ErrorCode: errorCode);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException)
        {
            return new StartupIdentityLookupResult(
                StartupIdentityLookupStatus.Unavailable);
        }
    }

    private async Task<OperationalResourceResult<T>> GetOperationalJsonAsync<T>(
        string requestUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var statusCode = response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                string? message = null;
                string? errorCode = null;
                try
                {
                    var error = await response.Content
                        .ReadFromJsonAsync<ApiResponse>(_jsonOptions, cancellationToken)
                        .ConfigureAwait(false);
                    message = error?.Message;
                    errorCode = error?.ErrorCode;
                }
                catch (JsonException)
                {
                    // The HTTP status remains authoritative for fail-closed handling.
                }

                return new OperationalResourceResult<T>(
                    IsTransientUnavailable(statusCode)
                        ? OperationalResourceStatus.Unavailable
                        : OperationalResourceStatus.Rejected,
                    HttpStatusCode: statusCode,
                    Message: message,
                    ErrorCode: errorCode);
            }

            try
            {
                var result = await response.Content
                    .ReadFromJsonAsync<ApiResponse<T>>(_jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return result?.Success == true && result.Data is not null
                    ? new OperationalResourceResult<T>(
                        OperationalResourceStatus.Success,
                        result.Data,
                        statusCode,
                        result.Message,
                        result.ErrorCode)
                    : new OperationalResourceResult<T>(
                        OperationalResourceStatus.Rejected,
                        HttpStatusCode: statusCode,
                        Message: result?.Message,
                        ErrorCode: result?.ErrorCode);
            }
            catch (JsonException)
            {
                return new OperationalResourceResult<T>(
                    OperationalResourceStatus.InvalidResponse,
                    HttpStatusCode: statusCode,
                    Message: "The server returned an invalid response.");
            }
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return new OperationalResourceResult<T>(
                OperationalResourceStatus.Unavailable);
        }
    }

    private async Task<OperationalResourceResult<TResponse>> PostOperationalJsonAsync<TRequest, TResponse>(
        string requestUri,
        TRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient
                .PostAsJsonAsync(requestUri, request, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            var statusCode = response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var errorPayload = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                var (message, errorCode) = ParseOperationalError(errorPayload);

                return new OperationalResourceResult<TResponse>(
                    IsTransientUnavailable(statusCode)
                        ? OperationalResourceStatus.Unavailable
                        : OperationalResourceStatus.Rejected,
                    HttpStatusCode: statusCode,
                    Message: message,
                    ErrorCode: errorCode);
            }

            try
            {
                var result = await response.Content
                    .ReadFromJsonAsync<ApiResponse<TResponse>>(_jsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return result?.Success == true && result.Data is not null
                    ? new OperationalResourceResult<TResponse>(
                        OperationalResourceStatus.Success,
                        result.Data,
                        statusCode,
                        result.Message,
                        result.ErrorCode)
                    : new OperationalResourceResult<TResponse>(
                        OperationalResourceStatus.InvalidResponse,
                        HttpStatusCode: statusCode,
                        Message: "The server did not return the created staff invite.");
            }
            catch (JsonException)
            {
                return new OperationalResourceResult<TResponse>(
                    OperationalResourceStatus.InvalidResponse,
                    HttpStatusCode: statusCode,
                    Message: "The server returned an invalid response.");
            }
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return new OperationalResourceResult<TResponse>(
                OperationalResourceStatus.Unavailable,
                Message: "The server could not be reached.");
        }
    }

    private static (string? Message, string? ErrorCode) ParseOperationalError(
        string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (null, null);
        }

        try
        {
            var apiError = JsonSerializer.Deserialize<ApiResponse>(payload, _jsonOptions);
            var apiMessage = apiError?.Errors is { Count: > 0 }
                ? string.Join(Environment.NewLine, apiError.Errors)
                : apiError?.Message;
            if (!string.IsNullOrWhiteSpace(apiMessage)
                || !string.IsNullOrWhiteSpace(apiError?.ErrorCode))
            {
                return (apiMessage, apiError?.ErrorCode);
            }
        }
        catch (JsonException)
        {
            // Fall through to RFC 7807 parsing.
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var messages = new List<string>();
            if (TryGetPropertyIgnoreCase(root, "errors", out var errors)
                && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in field.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String
                                && !string.IsNullOrWhiteSpace(item.GetString()))
                            {
                                messages.Add($"{field.Name}: {item.GetString()}");
                            }
                        }
                    }
                    else if (field.Value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(field.Value.GetString()))
                    {
                        messages.Add($"{field.Name}: {field.Value.GetString()}");
                    }
                }
            }

            if (messages.Count > 0)
            {
                return (string.Join(Environment.NewLine, messages), null);
            }

            if (TryGetPropertyIgnoreCase(root, "detail", out var detail)
                && detail.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                return (detail.GetString(), null);
            }

            if (TryGetPropertyIgnoreCase(root, "title", out var title)
                && title.ValueKind == JsonValueKind.String)
            {
                return (title.GetString(), null);
            }
        }
        catch (JsonException)
        {
            // A malformed body does not override the authoritative HTTP status.
        }

        return (null, null);
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsTransientUnavailable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static bool IsTransportFailure(Exception exception) =>
        exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException;

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

    public Task<OperationalResourceResult<StaffInviteProvisioningResponseDto>> ProvisionStaffAsync(
        CreateStaffInviteDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostOperationalJsonAsync<CreateStaffInviteDto, StaffInviteProvisioningResponseDto>(
            "users/staff",
            request,
            cancellationToken);
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

    // --- Invites ---

    public async Task<ApiResponse<AppInviteCodeResponseDto>> GenerateMemberInviteAsync(
        int memberId,
        CreateAppInviteDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await _httpClient.PostAsJsonAsync(
            $"members/{memberId}/app-invite",
            request,
            _jsonOptions);
        return await HandleResponseAsync<AppInviteCodeResponseDto>(response);
    }

    public async Task<ApiResponse<AppInviteResponseDto>> GetMemberInviteStatusAsync(int memberId)
    {
        var response = await _httpClient.GetAsync($"members/{memberId}/app-invite/status");
        return await HandleResponseAsync<AppInviteResponseDto>(response);
    }

    public async Task<ApiResponse> RevokeMemberInviteAsync(int memberId)
    {
        var response = await _httpClient.DeleteAsync($"members/{memberId}/app-invite");
        return await HandleResponseAsync(response);
    }

    // --- Attendance ---

    public async Task<ApiResponse<AttendanceDto>> CheckInAsync(string qrCode)
    {
        var request = new CheckInRequestDto
        {
            QrCode = qrCode,
            OperationId = Guid.NewGuid()
        };
        var response = await _httpClient.PostAsJsonAsync("attendance/check-in", request, _jsonOptions);
        return await HandleResponseAsync<AttendanceDto>(response);
    }

    public async Task<ApiResponse> CheckOutAsync(int attendanceId)
    {
        var request = new CheckOutRequestDto { OperationId = Guid.NewGuid() };
        var response = await _httpClient.PostAsJsonAsync(
            $"attendance/{attendanceId}/check-out",
            request,
            _jsonOptions);
        return await HandleResponseAsync(response);
    }

    public async Task<ApiResponse<IEnumerable<AttendanceDto>>> GetAttendanceByMemberIdAsync(int memberId)
    {
        var response = await _httpClient.GetAsync($"attendance/member/{memberId}");
        return await HandleResponseAsync<IEnumerable<AttendanceDto>>(response);
    }

    // --- Gym Goer Self-Service ---

    public async Task<ApiResponse<GoerDashboardDto>> GetGoerDashboardAsync()
    {
        var response = await _httpClient.GetAsync("me/dashboard");
        return await HandleResponseAsync<GoerDashboardDto>(response);
    }

    public Task<OperationalResourceResult<GoerDashboardDto>> GetGoerDashboardForRefreshAsync(
        CancellationToken cancellationToken = default) =>
        GetOperationalJsonAsync<GoerDashboardDto>("me/dashboard", cancellationToken);

    public async Task<ApiResponse<GoerDigitalCardDto>> GetGoerDigitalCardAsync()
    {
        var response = await _httpClient.GetAsync("me/digital-card");
        return await HandleResponseAsync<GoerDigitalCardDto>(response);
    }

    public async Task<ProfilePictureData?> GetCurrentProfilePictureAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await GetCurrentProfilePictureForRefreshAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.Status switch
        {
            OperationalResourceStatus.Success => result.Data,
            OperationalResourceStatus.Missing => null,
            OperationalResourceStatus.Unavailable =>
                throw new HttpRequestException("The profile picture service is unavailable."),
            OperationalResourceStatus.Rejected =>
                throw new UnauthorizedAccessException("Profile picture access was rejected."),
            _ => throw new InvalidDataException(
                result.Message ?? "The profile picture response is invalid.")
        };
    }

    public async Task<OperationalResourceResult<ProfilePictureData>>
        GetCurrentProfilePictureForRefreshAsync(
            CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "me/profile-picture");
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var statusCode = response.StatusCode;
            if (statusCode == HttpStatusCode.NotFound)
            {
                return new OperationalResourceResult<ProfilePictureData>(
                    OperationalResourceStatus.Missing,
                    HttpStatusCode: statusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new OperationalResourceResult<ProfilePictureData>(
                    IsTransientUnavailable(statusCode)
                        ? OperationalResourceStatus.Unavailable
                        : OperationalResourceStatus.Rejected,
                    HttpStatusCode: statusCode);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is not ("image/jpeg" or "image/png"))
            {
                return new OperationalResourceResult<ProfilePictureData>(
                    OperationalResourceStatus.InvalidResponse,
                    HttpStatusCode: statusCode,
                    Message: "The profile picture response is not a supported image.");
            }

            if (response.Content.Headers.ContentLength is > MaximumProfilePictureBytes)
            {
                return new OperationalResourceResult<ProfilePictureData>(
                    OperationalResourceStatus.InvalidResponse,
                    HttpStatusCode: statusCode,
                    Message: "The profile picture response is too large.");
            }

            var bytes = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (bytes.Length is 0 or > MaximumProfilePictureBytes)
            {
                return new OperationalResourceResult<ProfilePictureData>(
                    OperationalResourceStatus.InvalidResponse,
                    HttpStatusCode: statusCode,
                    Message: "The profile picture response has an invalid size.");
            }

            return new OperationalResourceResult<ProfilePictureData>(
                OperationalResourceStatus.Success,
                new ProfilePictureData(bytes, contentType),
                statusCode);
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            return new OperationalResourceResult<ProfilePictureData>(
                OperationalResourceStatus.Unavailable);
        }
    }

    public async Task<ApiResponse<CurrentAttendanceStateDto>> GetGoerCurrentAttendanceAsync()
    {
        var response = await _httpClient.GetAsync("me/attendance/current");
        return await HandleResponseAsync<CurrentAttendanceStateDto>(response);
    }

    public Task<OperationalResourceResult<CurrentAttendanceStateDto>>
        GetGoerCurrentAttendanceForRefreshAsync(
            CancellationToken cancellationToken = default) =>
        GetOperationalJsonAsync<CurrentAttendanceStateDto>(
            "me/attendance/current",
            cancellationToken);

    public async Task<ApiResponse<AttendanceHistoryPageDto>> GetGoerAttendanceHistoryAsync(
        DateOnly? fromGymDate,
        DateOnly? endExclusiveGymDate,
        int page = 1,
        int pageSize = 10)
    {
        var response = await _httpClient.GetAsync(BuildGoerAttendanceHistoryQuery(
            fromGymDate,
            endExclusiveGymDate,
            page,
            pageSize));
        return await HandleResponseAsync<AttendanceHistoryPageDto>(response);
    }

    public Task<OperationalResourceResult<AttendanceHistoryPageDto>>
        GetGoerAttendanceHistoryForRefreshAsync(
            DateOnly? fromGymDate,
            DateOnly? endExclusiveGymDate,
            int page = 1,
            int pageSize = 10,
            CancellationToken cancellationToken = default) =>
        GetOperationalJsonAsync<AttendanceHistoryPageDto>(
            BuildGoerAttendanceHistoryQuery(
                fromGymDate,
                endExclusiveGymDate,
                page,
                pageSize),
            cancellationToken);

    private static string BuildGoerAttendanceHistoryQuery(
        DateOnly? fromGymDate,
        DateOnly? endExclusiveGymDate,
        int page,
        int pageSize)
    {
        var query = $"me/attendance?page={page}&pageSize={pageSize}";
        if (fromGymDate.HasValue) query += $"&from={fromGymDate.Value:yyyy-MM-dd}";
        if (endExclusiveGymDate.HasValue) query += $"&to={endExclusiveGymDate.Value:yyyy-MM-dd}";
        return query;
    }

    public async Task<ApiResponse<AttendanceDto>> GoerCheckInAsync(Guid operationId)
    {
        var dto = new AttendanceOperationRequestDto { OperationId = operationId };
        var response = await _httpClient.PostAsJsonAsync("me/attendance/check-in", dto, _jsonOptions);
        return await HandleResponseAsync<AttendanceDto>(response);
    }

    public async Task<ApiResponse<AttendanceDto>> GoerCheckOutAsync(Guid operationId)
    {
        var dto = new CheckOutRequestDto { OperationId = operationId };
        var response = await _httpClient.PostAsJsonAsync("me/attendance/checkout", dto, _jsonOptions);
        return await HandleResponseAsync<AttendanceDto>(response);
    }

    public async Task<ApiResponse<GoerProgressDto>> GetGoerProgressAsync(string month)
    {
        var response = await _httpClient.GetAsync($"me/progress?month={month}");
        return await HandleResponseAsync<GoerProgressDto>(response);
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

    public async Task<ApiResponse<IEnumerable<SubscriptionResponseDto>>> GetSubscriptionsByMemberIdAsync(int memberId)
    {
        var response = await _httpClient.GetAsync($"subscriptions/member/{memberId}");
        return await HandleResponseAsync<IEnumerable<SubscriptionResponseDto>>(response);
    }

    // --- Settings ---

    public async Task<ApiResponse<IEnumerable<SystemSettingDto>>> GetSettingsAsync()
    {
        var response = await _httpClient.GetAsync("settings");
        return await HandleResponseAsync<IEnumerable<SystemSettingDto>>(response);
    }

    public async Task<ApiResponse> UpdateSettingAsync(string key, string value)
    {
        var dto = new UpdateSettingDto { SettingValue = value };
        var response = await _httpClient.PutAsJsonAsync($"settings/{key}", dto);
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

    public async Task<ApiResponse<IEnumerable<MonthlyRevenueReportDto>>> GetMonthlyRevenueReportAsync(DateTime startDate, DateTime endDate)
    {
        var response = await _httpClient.GetAsync($"reports/monthly-revenue?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        return await HandleResponseAsync<IEnumerable<MonthlyRevenueReportDto>>(response);
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

    public async Task<byte[]> ExportMonthlyRevenueCsvAsync(DateTime startDate, DateTime endDate)
    {
        var response = await _httpClient.GetAsync($"reports/monthly-revenue/export?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
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

    public async Task<byte[]> ExportExpiringMembershipsCsvAsync(int nextDays = 7)
    {
        var response = await _httpClient.GetAsync($"reports/expiring-memberships/export?nextDays={nextDays}");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : Array.Empty<byte>();
    }

    // --- Owner Analytics ---

    public async Task<ApiResponse<OwnerAttendanceSummaryDto>> GetOwnerAttendanceSummaryAsync(DateTime? from, DateTime? to, string bucket = "day")
    {
        var query = $"reports/attendance/summary?bucket={bucket}";
        if (from.HasValue) query += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) query += $"&to={to.Value:yyyy-MM-dd}";

        var response = await _httpClient.GetAsync(query);
        return await HandleResponseAsync<OwnerAttendanceSummaryDto>(response);
    }

    public async Task<byte[]> ExportOwnerAttendanceSummaryCsvAsync(DateTime? from, DateTime? to, string bucket = "day")
    {
        var query = $"reports/attendance/summary/export?bucket={bucket}";
        if (from.HasValue) query += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) query += $"&to={to.Value:yyyy-MM-dd}";

        var response = await _httpClient.GetAsync(query);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : Array.Empty<byte>();
    }
}
