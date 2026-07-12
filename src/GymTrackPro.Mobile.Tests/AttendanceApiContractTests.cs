using System.Net;
using System.Text;
using System.Text.Json;
using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile.Tests;

public sealed class AttendanceApiContractTests
{
    [Fact]
    public async Task Staff_attendance_uses_canonical_routes_and_operation_bodies()
    {
        var bodies = new List<string>();
        var handler = new RecordingHandler(async request =>
        {
            bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync());
            return JsonResponse(request.RequestUri!.AbsolutePath.EndsWith("check-in", StringComparison.Ordinal)
                ? SuccessAttendance
                : """{"success":true,"message":"ok"}""");
        });
        var service = CreateService(handler);

        var checkIn = await service.CheckInAsync("member-qr");
        var checkOut = await service.CheckOutAsync(17);

        Assert.True(checkIn.Success);
        Assert.True(checkOut.Success);
        Assert.Equal("/api/v1/attendance/check-in", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/api/v1/attendance/17/check-out", handler.Requests[1].RequestUri!.AbsolutePath);
        using var checkInBody = JsonDocument.Parse(bodies[0]);
        Assert.Equal("member-qr", checkInBody.RootElement.GetProperty("qrCode").GetString());
        Assert.NotEqual(Guid.Empty, checkInBody.RootElement.GetProperty("operationId").GetGuid());
        using var checkOutBody = JsonDocument.Parse(bodies[1]);
        Assert.NotEqual(Guid.Empty, checkOutBody.RootElement.GetProperty("operationId").GetGuid());
    }

    [Fact]
    public async Task Self_attendance_uses_operation_only_checkin_and_checkout_contracts()
    {
        var bodies = new List<string>();
        var handler = new RecordingHandler(async request =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return JsonResponse(SuccessAttendance);
        });
        var service = CreateService(handler);
        var checkInOperation = Guid.NewGuid();
        var checkOutOperation = Guid.NewGuid();

        await service.GoerCheckInAsync(checkInOperation);
        await service.GoerCheckOutAsync(checkOutOperation);

        Assert.Equal("/api/v1/me/attendance/check-in", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("/api/v1/me/attendance/checkout", handler.Requests[1].RequestUri!.AbsolutePath);
        using var checkInBody = JsonDocument.Parse(bodies[0]);
        Assert.Equal(checkInOperation, checkInBody.RootElement.GetProperty("operationId").GetGuid());
        Assert.False(checkInBody.RootElement.TryGetProperty("qrCode", out _));
        using var checkOutBody = JsonDocument.Parse(bodies[1]);
        Assert.Equal(checkOutOperation, checkOutBody.RootElement.GetProperty("operationId").GetGuid());
    }

    [Fact]
    public async Task Current_and_history_deserialize_the_exact_server_contracts()
    {
        var handler = new RecordingHandler(request => Task.FromResult(JsonResponse(
            request.RequestUri!.AbsolutePath.EndsWith("current", StringComparison.Ordinal)
                ? """{"success":true,"data":{"state":"CheckedIn","session":{"attendanceID":9,"memberID":3,"attendanceDate":"2026-07-12","checkInTime":"2026-07-12T01:00:00Z"}}}"""
                : """{"success":true,"data":{"items":[{"attendanceID":9,"memberID":3,"attendanceDate":"2026-07-12","checkInTime":"2026-07-12T01:00:00Z"}],"totalCount":1,"page":1,"pageSize":10,"totalPages":1,"fromGymDate":"2026-06-13","endExclusiveGymDate":"2026-07-13"}}""")));
        var service = CreateService(handler);

        var current = await service.GetGoerCurrentAttendanceAsync();
        var history = await service.GetGoerAttendanceHistoryAsync(null, null);

        Assert.Equal(9, current.Data!.Session!.AttendanceID);
        Assert.Single(history.Data!.Items);
        Assert.Equal(1, history.Data.TotalCount);
    }

    [Fact]
    public async Task Failure_preserves_structured_error_metadata()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(JsonResponse(
            """{"success":false,"message":"Conflict","errorCode":"ACTIVE_SESSION_EXISTS","errors":["Already checked in"]}""",
            HttpStatusCode.Conflict)));
        var service = CreateService(handler);

        var response = await service.GoerCheckInAsync(Guid.NewGuid());

        Assert.False(response.Success);
        Assert.Equal("ACTIVE_SESSION_EXISTS", response.ErrorCode);
        Assert.Equal(new[] { "Already checked in" }, response.Errors);
    }

    [Fact]
    public async Task Current_profile_picture_uses_protected_self_route_without_caching()
    {
        var expected = new byte[] { 0xFF, 0xD8, 0xFF, 0x01 };
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg") }
            }
        }));
        var service = CreateService(handler);

        var picture = await service.GetCurrentProfilePictureAsync();

        Assert.NotNull(picture);
        Assert.Equal(expected, picture.Bytes);
        Assert.Equal("image/jpeg", picture.ContentType);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v1/me/profile-picture", request.RequestUri!.AbsolutePath);
        Assert.True(request.Headers.CacheControl!.NoCache);
        Assert.True(request.Headers.CacheControl.NoStore);
    }

    [Fact]
    public async Task Missing_current_profile_picture_returns_default_signal()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = CreateService(handler);

        var picture = await service.GetCurrentProfilePictureAsync();

        Assert.Null(picture);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Startup_identity_authorization_or_business_failure_is_rejected(
        HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(JsonResponse(
            """{"success":false,"errorCode":"APP_IDENTITY_REJECTED"}""",
            statusCode)));
        var service = CreateService(handler);

        var result = await service.GetCurrentUserForStartupAsync();

        Assert.Equal(StartupIdentityLookupStatus.Rejected, result.Status);
        Assert.Equal(statusCode, result.HttpStatusCode);
        Assert.Equal("APP_IDENTITY_REJECTED", result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Startup_identity_transient_server_failure_allows_offline_classification(
        HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(statusCode)));
        var service = CreateService(handler);

        var result = await service.GetCurrentUserForStartupAsync();

        Assert.Equal(StartupIdentityLookupStatus.Unavailable, result.Status);
        Assert.Equal(statusCode, result.HttpStatusCode);
    }

    [Fact]
    public async Task Startup_identity_transport_failure_allows_offline_classification()
    {
        var handler = new RecordingHandler(_ =>
            throw new HttpRequestException("Simulated offline transport."));
        var service = CreateService(handler);

        var result = await service.GetCurrentUserForStartupAsync();

        Assert.Equal(StartupIdentityLookupStatus.Unavailable, result.Status);
        Assert.Null(result.HttpStatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Operational_dashboard_authorization_failure_is_rejected(
        HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(JsonResponse(
            """{"success":false,"errorCode":"APP_ACCESS_REVOKED"}""",
            statusCode)));
        var service = CreateService(handler);

        var result = await service.GetGoerDashboardForRefreshAsync();

        Assert.Equal(OperationalResourceStatus.Rejected, result.Status);
        Assert.Equal(statusCode, result.HttpStatusCode);
        Assert.Equal("APP_ACCESS_REVOKED", result.ErrorCode);
    }

    [Fact]
    public async Task Operational_dashboard_transport_failure_is_unavailable()
    {
        var handler = new RecordingHandler(_ =>
            throw new HttpRequestException("Simulated unavailable API."));
        var service = CreateService(handler);

        var result = await service.GetGoerDashboardForRefreshAsync();

        Assert.Equal(OperationalResourceStatus.Unavailable, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Operational_profile_authorization_failure_is_rejected(
        HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(statusCode)));
        var service = CreateService(handler);

        var result = await service.GetCurrentProfilePictureForRefreshAsync();

        Assert.Equal(OperationalResourceStatus.Rejected, result.Status);
        Assert.Equal(statusCode, result.HttpStatusCode);
    }

    [Fact]
    public async Task Operational_profile_transport_failure_is_unavailable()
    {
        var handler = new RecordingHandler(_ =>
            throw new HttpRequestException("Simulated unavailable image API."));
        var service = CreateService(handler);

        var result = await service.GetCurrentProfilePictureForRefreshAsync();

        Assert.Equal(OperationalResourceStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Operational_profile_invalid_content_is_optional_invalid_response_not_rejection()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-an-image", Encoding.UTF8, "text/plain")
            }));
        var service = CreateService(handler);

        var result = await service.GetCurrentProfilePictureForRefreshAsync();

        Assert.Equal(OperationalResourceStatus.InvalidResponse, result.Status);
        Assert.NotEqual(OperationalResourceStatus.Rejected, result.Status);
    }

    private static ApiService CreateService(HttpMessageHandler handler) => new(new HttpClient(handler)
    {
        BaseAddress = new Uri("https://api.example.test/api/v1/")
    });

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private const string SuccessAttendance =
        """{"success":true,"data":{"attendanceID":17,"memberID":3,"attendanceDate":"2026-07-12","checkInTime":"2026-07-12T01:00:00Z"}}""";

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await response(request);
        }
    }
}
