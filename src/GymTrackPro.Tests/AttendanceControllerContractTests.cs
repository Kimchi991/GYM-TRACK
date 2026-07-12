using System.Reflection;
using GymTrackPro.API.Authorization;
using GymTrackPro.API.Controllers;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using GymTrackPro.Shared.Enums;
using GymTrackPro.API.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using GymTrackPro.API.Authentication;
using GymTrackPro.Shared.Constants;

namespace GymTrackPro.Tests;

public class AttendanceControllerContractTests
{
    [Fact]
    public async Task Report_controller_preserves_stable_bad_request_for_maximum_inclusive_end()
    {
        var maximumDate = new DateTime(9999, 12, 31);
        var service = new Mock<IReportsService>();
        service.Setup(item => item.GetAttendanceReportAsync(maximumDate, maximumDate))
            .ThrowsAsync(new AppAccessException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.InvalidAttendanceRange,
                "The report date range is outside the supported range."));
        var controller = new ReportsController(service.Object);

        var exception = await Assert.ThrowsAsync<AppAccessException>(() =>
            controller.GetAttendance(maximumDate, maximumDate));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal(ErrorCodes.InvalidAttendanceRange, exception.ErrorCode);
    }

    [Fact]
    public void Owned_controllers_use_frozen_named_policies()
    {
        Assert.Equal(Policies.GymGoerSelf, ClassPolicy<MeAttendanceController>());
        Assert.Equal(Policies.GymGoerSelf, ClassPolicy<MeDashboardController>());
        Assert.Equal(Policies.GymGoerSelf, ClassPolicy<MeProgressController>());
        Assert.Equal(Policies.OwnerOnly, ClassPolicy<ReportsController>());

        Assert.Equal(Policies.BackOffice, ActionPolicy<AttendanceController>(nameof(AttendanceController.GetById)));
        Assert.Equal(Policies.BackOffice, ActionPolicy<AttendanceController>(nameof(AttendanceController.CheckIn)));
        Assert.Equal(Policies.BackOffice, ActionPolicy<AttendanceController>(nameof(AttendanceController.CheckOut)));
        Assert.Equal(Policies.BackOffice, ActionPolicy<AttendanceController>(nameof(AttendanceController.GetByMemberId)));
        Assert.Equal(Policies.BackOffice, ActionPolicy<AttendanceController>(nameof(AttendanceController.GetMemberHistory)));
        Assert.Equal(Policies.OwnerOnly, ActionPolicy<AttendanceController>(nameof(AttendanceController.CorrectCheckout)));
        Assert.Equal(Policies.OwnerOnly, ActionPolicy<AttendanceController>(nameof(AttendanceController.Void)));
    }

    [Fact]
    public void Self_routes_match_frozen_contract()
    {
        var controllerRoute = typeof(MeAttendanceController)
            .GetCustomAttribute<RouteAttribute>()!;
        var historyRoute = Method<MeAttendanceController>(nameof(MeAttendanceController.GetHistory))
            .GetCustomAttribute<HttpGetAttribute>()!;
        var currentRoute = Method<MeAttendanceController>(nameof(MeAttendanceController.GetCurrentSession))
            .GetCustomAttribute<HttpGetAttribute>()!;
        var checkoutRoute = Method<MeAttendanceController>(nameof(MeAttendanceController.CheckOutCurrentSession))
            .GetCustomAttribute<HttpPostAttribute>()!;

        Assert.Equal("api/v1/me/attendance", controllerRoute.Template);
        Assert.Null(historyRoute.Template);
        Assert.Equal("current", currentRoute.Template);
        Assert.Equal("checkout", checkoutRoute.Template);

        var parameters = Method<MeAttendanceController>(nameof(MeAttendanceController.GetHistory))
            .GetParameters();
        Assert.Equal("from", parameters[0].GetCustomAttribute<FromQueryAttribute>()?.Name);
        Assert.Equal("to", parameters[1].GetCustomAttribute<FromQueryAttribute>()?.Name);

        var graphParameters = Method<ReportsController>(nameof(ReportsController.GetAttendanceSummary))
            .GetParameters();
        Assert.Equal("from", graphParameters[0].GetCustomAttribute<FromQueryAttribute>()?.Name);
        Assert.Equal("to", graphParameters[1].GetCustomAttribute<FromQueryAttribute>()?.Name);
        Assert.Equal("bucket", graphParameters[2].GetCustomAttribute<FromQueryAttribute>()?.Name);
    }

    [Fact]
    public async Task Current_session_returns_explicit_checked_out_state_instead_of_not_found()
    {
        var service = new Mock<IAttendanceService>();
        service.Setup(item => item.GetCurrentOpenSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AttendanceDto?)null);
        var controller = new MeAttendanceController(service.Object);

        var action = await controller.GetCurrentSession(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var response = Assert.IsType<ApiResponse<CurrentAttendanceStateDto>>(ok.Value);
        Assert.NotNull(response.Data);
        Assert.False(response.Data.IsCheckedIn);
        Assert.Equal(AttendanceSessionState.CheckedOut, response.Data.State);
        Assert.Null(response.Data.Session);
    }

    [Fact]
    public async Task Legacy_adapter_emits_deprecation_and_sunset_headers()
    {
        var service = new Mock<IAttendanceService>();
        service.Setup(item => item.CheckInAsync("qr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttendanceDto { AttendanceID = 7 });
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.SetupGet(context => context.UserId).Returns(5);
        var logger = new Mock<ILogger<AttendanceController>>();
        var controller = new AttendanceController(service.Object, currentUser.Object, logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

#pragma warning disable CS0618
        await controller.CheckInLegacy("qr", CancellationToken.None);
#pragma warning restore CS0618

        Assert.Equal("true", controller.Response.Headers["Deprecation"]);
        Assert.Equal("Tue, 12 Jan 2027 00:00:00 GMT", controller.Response.Headers["Sunset"]);
        Assert.Contains("successor-version", controller.Response.Headers["Link"].ToString());
        logger.Verify(
            item => item.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(
                    "ActorUserId: 5",
                    StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData("normal", "\"normal\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("  +SUM(1,2)", "\"'  +SUM(1,2)\"")]
    [InlineData("@cmd", "\"'@cmd\"")]
    public void Csv_encoder_quotes_and_neutralizes_formula_cells(string input, string expected)
    {
        Assert.Equal(expected, CsvCellEncoder.Encode(input));
    }

    [Fact]
    public async Task Legacy_staff_history_route_serializes_data_as_array_and_points_to_paged_successor()
    {
        var service = new Mock<IAttendanceService>();
        service.Setup(item => item.GetLegacyMemberHistoryAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AttendanceDto { AttendanceID = 4, MemberID = 9 } });
        var currentUser = new Mock<ICurrentUserContext>();
        currentUser.SetupGet(context => context.UserId).Returns(5);
        var controller = new AttendanceController(
            service.Object,
            currentUser.Object,
            Mock.Of<ILogger<AttendanceController>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

#pragma warning disable CS0618
        var action = await controller.GetByMemberId(9);
#pragma warning restore CS0618

        var ok = Assert.IsType<OkObjectResult>(action);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            ok.Value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Equal(4, data[0].GetProperty("attendanceID").GetInt32());
        Assert.Equal("true", controller.Response.Headers["Deprecation"]);
        Assert.Contains("/api/v1/attendance/member/9/history", controller.Response.Headers["Link"].ToString());
    }

    private static string ClassPolicy<TController>()
    {
        return typeof(TController).GetCustomAttribute<AuthorizeAttribute>()?.Policy
            ?? string.Empty;
    }

    private static string ActionPolicy<TController>(string methodName)
    {
        return Method<TController>(methodName).GetCustomAttribute<AuthorizeAttribute>()?.Policy
            ?? string.Empty;
    }

    private static MethodInfo Method<TController>(string methodName)
    {
        return typeof(TController).GetMethods()
            .Single(method => method.Name == methodName);
    }
}
