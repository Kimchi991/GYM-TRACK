using System.Net;
using System.Text;
using System.Text.Json;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.Tests;

public sealed class MemberInviteApiContractTests
{
    [Fact]
    public async Task Generate_posts_required_purpose_and_deserializes_canonical_invite_contracts()
    {
        const string expectedPurpose = "Member mobile app access";
        string? requestBody = null;
        var handler = new RecordingHandler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                requestBody = await request.Content!.ReadAsStringAsync();
                return JsonResponse(
                    """
                    {
                      "success": true,
                      "message": "Member invite created.",
                      "data": {
                        "inviteCode": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAE",
                        "details": {
                          "targetMemberID": 42,
                          "targetUserID": null,
                          "intendedRole": "GymGoer",
                          "purpose": "Member mobile app access",
                          "expiresAtUtc": "2026-07-15T08:00:00Z",
                          "status": "Unused",
                          "usedAtUtc": null,
                          "revokedAtUtc": null,
                          "createdAtUtc": "2026-07-12T08:00:00Z"
                        }
                      }
                    }
                    """);
            }

            return JsonResponse(
                """
                {
                  "success": true,
                  "message": "Member invite status retrieved.",
                  "data": {
                    "targetMemberID": 42,
                    "targetUserID": null,
                    "intendedRole": "GymGoer",
                    "purpose": "Member mobile app access",
                    "expiresAtUtc": "2026-07-15T08:00:00Z",
                    "status": "Revoked",
                    "usedAtUtc": "2026-07-12T09:00:00Z",
                    "revokedAtUtc": "2026-07-12T10:00:00Z",
                    "createdAtUtc": "2026-07-12T08:00:00Z"
                  }
                }
                """);
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/api/v1/")
        };
        var service = new ApiService(client);

        var generated = await service.GenerateMemberInviteAsync(
            42,
            new CreateAppInviteDto { Purpose = expectedPurpose });
        var status = await service.GetMemberInviteStatusAsync(42);

        Assert.NotNull(requestBody);
        using var body = JsonDocument.Parse(requestBody!);
        Assert.Equal(expectedPurpose, body.RootElement.GetProperty("purpose").GetString());
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(
            "https://api.example.test/api/v1/members/42/app-invite",
            handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.True(generated.Success);
        Assert.Equal(expectedPurpose, generated.Data!.Details.Purpose);
        Assert.Equal(DateTimeKind.Utc, generated.Data.Details.ExpiresAtUtc.Kind);
        Assert.Equal(new DateTime(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc),
            generated.Data.Details.ExpiresAtUtc);
        Assert.True(status.Success);
        Assert.Equal(DateTimeKind.Utc, status.Data!.UsedAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, status.Data.RevokedAtUtc!.Value.Kind);
        Assert.Equal(new DateTime(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc),
            status.Data.RevokedAtUtc);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _response;

        public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response)
        {
            _response = response;
        }

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
