using System.Net;
using System.Text;
using System.Text.Json;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.Tests;

public sealed class StaffProvisioningApiContractTests
{
    [Fact]
    public async Task Provision_posts_exact_owner_staff_contract_and_reads_created_invite()
    {
        string? requestBody = null;
        var handler = new RecordingHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                HttpStatusCode.Created,
                """
                {
                  "success": true,
                  "message": "Receptionist profile and invite created.",
                  "data": {
                    "user": {
                      "userID": 8,
                      "email": "receptionist@example.com",
                      "firstName": "Ria",
                      "lastName": "Santos",
                      "role": "Receptionist",
                      "isActive": true
                    },
                    "invite": {
                      "inviteCode": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAE",
                      "details": {
                        "targetUserID": 8,
                        "intendedRole": "Receptionist",
                        "purpose": "Receptionist mobile app access",
                        "expiresAtUtc": "2026-07-15T08:00:00Z",
                        "status": "Unused"
                      }
                    }
                  }
                }
                """);
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/api/v1/")
        };
        var service = new ApiService(client);

        var result = await service.ProvisionStaffAsync(new CreateStaffInviteDto
        {
            FirstName = "Ria",
            LastName = "Santos",
            Email = "receptionist@example.com",
            Purpose = "Receptionist mobile app access"
        });

        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(
            "https://api.example.test/api/v1/users/staff",
            handler.Requests[0].RequestUri!.AbsoluteUri);
        using var body = JsonDocument.Parse(requestBody!);
        Assert.Equal("Ria", body.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("Santos", body.RootElement.GetProperty("lastName").GetString());
        Assert.Equal("receptionist@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("Receptionist mobile app access", body.RootElement.GetProperty("purpose").GetString());
        Assert.False(body.RootElement.TryGetProperty("role", out _));
        Assert.False(body.RootElement.TryGetProperty("password", out _));
        Assert.Equal(OperationalResourceStatus.Success, result.Status);
        Assert.Equal(HttpStatusCode.Created, result.HttpStatusCode);
        Assert.Equal("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAE", result.Data!.Invite.InviteCode);
    }

    [Fact]
    public async Task Provision_preserves_forbidden_status_and_server_message()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.Forbidden,
            """
            {
              "success": false,
              "message": "Owner access is required.",
              "errorCode": "FORBIDDEN"
            }
            """)));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/api/v1/")
        };

        var result = await new ApiService(client).ProvisionStaffAsync(new CreateStaffInviteDto());

        Assert.Equal(OperationalResourceStatus.Rejected, result.Status);
        Assert.Equal(HttpStatusCode.Forbidden, result.HttpStatusCode);
        Assert.Equal("FORBIDDEN", result.ErrorCode);
        Assert.Equal("Owner access is required.", result.Message);
    }

    [Fact]
    public async Task Provision_flattens_validation_problem_field_errors()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.BadRequest,
            """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
              "title": "One or more validation errors occurred.",
              "status": 400,
              "errors": {
                "Email": ["The Email field is not a valid e-mail address."],
                "Purpose": ["The field Purpose must be a string with a minimum length of 1."]
              },
              "traceId": "00-test"
            }
            """)));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/api/v1/")
        };

        var result = await new ApiService(client).ProvisionStaffAsync(new CreateStaffInviteDto());

        Assert.Equal(OperationalResourceStatus.Rejected, result.Status);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Contains("Email: The Email field is not a valid e-mail address.", result.Message, StringComparison.Ordinal);
        Assert.Contains("Purpose: The field Purpose must be a string", result.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _response;

        public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) =>
            _response = response;

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await _response(request);
        }
    }
}
