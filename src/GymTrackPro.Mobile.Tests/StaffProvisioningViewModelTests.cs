using System.Net;
using System.Reflection;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Mobile.ViewModels;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Mobile.Tests;

public sealed class StaffProvisioningViewModelTests
{
    [Fact]
    public async Task Success_uses_trimmed_request_and_exposes_ephemeral_invite_for_copy()
    {
        CreateStaffInviteDto? submitted = null;
        var api = CreateApi((method, arguments) => method.Name switch
        {
            nameof(IApiService.GetCurrentUserForStartupAsync) => Task.FromResult(OwnerIdentity()),
            nameof(IApiService.ProvisionStaffAsync) => Task.FromResult(
                Success((CreateStaffInviteDto)arguments![0]!)),
            _ => throw new NotSupportedException(method.Name)
        });
        OperationalResourceResult<StaffInviteProvisioningResponseDto> Success(
            CreateStaffInviteDto request)
        {
            submitted = request;
            return new(
                OperationalResourceStatus.Success,
                new StaffInviteProvisioningResponseDto
                {
                    User = new UserResponseDto
                    {
                        UserID = 8,
                        FirstName = "Ria",
                        LastName = "Santos",
                        Email = request.Email,
                        Role = UserRole.Receptionist
                    },
                    Invite = new AppInviteCodeResponseDto
                    {
                        InviteCode = "one-time-code",
                        Details = new AppInviteResponseDto
                        {
                            IntendedRole = UserRole.Receptionist,
                            ExpiresAtUtc = new DateTime(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc)
                        }
                    }
                },
                HttpStatusCode.Created);
        }
        var clipboard = new RecordingClipboard();
        var dialog = new RecordingDialog();
        var viewModel = new StaffProvisioningViewModel(api, clipboard, dialog)
        {
            FirstName = "  Ria ",
            LastName = " Santos  ",
            Email = " ｒｉａ@example.com ",
            Purpose = " Receptionist mobile app access "
        };

        await viewModel.InitializeAsync();
        await viewModel.ProvisionStaffAsync();
        await viewModel.CopyInviteAsync();

        Assert.Equal("Ria", submitted!.FirstName);
        Assert.Equal("Santos", submitted.LastName);
        Assert.Equal("ria@example.com", submitted.Email);
        Assert.True(viewModel.HasGeneratedInvite);
        Assert.False(viewModel.CanProvision);
        Assert.Equal("one-time-code", viewModel.InviteCode);
        Assert.Contains("2026-07-15 08:00 UTC", viewModel.ExpiresAtText, StringComparison.Ordinal);
        Assert.Contains("ria@example.com", viewModel.RecipientInstruction, StringComparison.Ordinal);
        Assert.Equal("one-time-code", clipboard.CopiedText);
        Assert.Equal(1, dialog.AlertCount);
    }

    [Theory]
    [InlineData("", "Santos", "receptionist@example.com", "Purpose")]
    [InlineData("Ria", "Santos", "not-an-email", "Purpose")]
    [InlineData("Ria", "Santos", "receptionist@example.com", "")]
    public async Task Invalid_form_never_calls_provisioning_api(
        string firstName,
        string lastName,
        string email,
        string purpose)
    {
        var provisionCalls = 0;
        var api = CreateApi((method, _) => method.Name switch
        {
            nameof(IApiService.GetCurrentUserForStartupAsync) => Task.FromResult(OwnerIdentity()),
            nameof(IApiService.ProvisionStaffAsync) => CountProvision(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<OperationalResourceResult<StaffInviteProvisioningResponseDto>> CountProvision()
        {
            provisionCalls++;
            return Task.FromResult(new OperationalResourceResult<StaffInviteProvisioningResponseDto>(
                OperationalResourceStatus.Success));
        }
        var viewModel = CreateViewModel(api);
        viewModel.FirstName = firstName;
        viewModel.LastName = lastName;
        viewModel.Email = email;
        viewModel.Purpose = purpose;

        await viewModel.InitializeAsync();
        await viewModel.ProvisionStaffAsync();

        Assert.Equal(0, provisionCalls);
        Assert.NotEmpty(viewModel.ErrorMessage);
        Assert.False(viewModel.HasGeneratedInvite);
    }

    [Fact]
    public async Task Forbidden_response_fails_closed_without_exposing_an_invite()
    {
        var api = CreateApi((method, _) => method.Name switch
        {
            nameof(IApiService.GetCurrentUserForStartupAsync) => Task.FromResult(OwnerIdentity()),
            nameof(IApiService.ProvisionStaffAsync) => Task.FromResult(
                new OperationalResourceResult<StaffInviteProvisioningResponseDto>(
                    OperationalResourceStatus.Rejected,
                    HttpStatusCode: HttpStatusCode.Forbidden,
                    Message: "Forbidden")),
            _ => throw new NotSupportedException(method.Name)
        });
        var viewModel = ValidViewModel(api);

        await viewModel.InitializeAsync();
        await viewModel.ProvisionStaffAsync();

        Assert.False(viewModel.CanProvision);
        Assert.False(viewModel.HasGeneratedInvite);
        Assert.Empty(viewModel.InviteCode);
        Assert.Contains("Only the gym owner", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_submit_is_guarded_and_posts_only_once()
    {
        var provisionCalls = 0;
        var responseGate = new TaskCompletionSource<OperationalResourceResult<StaffInviteProvisioningResponseDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var api = CreateApi((method, _) => method.Name switch
        {
            nameof(IApiService.GetCurrentUserForStartupAsync) => Task.FromResult(OwnerIdentity()),
            nameof(IApiService.ProvisionStaffAsync) => CountAndWait(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<OperationalResourceResult<StaffInviteProvisioningResponseDto>> CountAndWait()
        {
            provisionCalls++;
            return responseGate.Task;
        }
        var viewModel = ValidViewModel(api);
        await viewModel.InitializeAsync();

        var first = viewModel.ProvisionStaffAsync();
        var second = viewModel.ProvisionStaffAsync();
        Assert.Equal(1, provisionCalls);
        Assert.True(second.IsCompletedSuccessfully);

        responseGate.SetResult(new OperationalResourceResult<StaffInviteProvisioningResponseDto>(
            OperationalResourceStatus.Rejected,
            HttpStatusCode: HttpStatusCode.BadRequest,
            Message: "Validation failed."));
        await Task.WhenAll(first, second);

        Assert.Equal(1, provisionCalls);
    }

    [Fact]
    public async Task Unexpected_identity_provider_failure_is_contained_and_fails_closed()
    {
        var api = CreateApi((method, _) => method.Name switch
        {
            nameof(IApiService.GetCurrentUserForStartupAsync) =>
                throw new InvalidOperationException("provider failure"),
            _ => throw new NotSupportedException(method.Name)
        });
        var viewModel = ValidViewModel(api);

        var exception = await Record.ExceptionAsync(viewModel.InitializeAsync);

        Assert.Null(exception);
        Assert.False(viewModel.CanProvision);
        Assert.Contains("provisioning remains locked", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivate_clears_invite_and_requires_fresh_owner_authorization()
    {
        var identityCalls = 0;
        var api = CreateApi((method, arguments) => method.Name switch
        {
            nameof(IApiService.GetCurrentUserForStartupAsync) => Identity(),
            nameof(IApiService.ProvisionStaffAsync) => Task.FromResult(
                SuccessfulProvision((CreateStaffInviteDto)arguments![0]!)),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<StartupIdentityLookupResult> Identity()
        {
            identityCalls++;
            return Task.FromResult(OwnerIdentity());
        }
        var viewModel = ValidViewModel(api);
        await viewModel.InitializeAsync();
        await viewModel.ProvisionStaffAsync();
        Assert.True(viewModel.HasGeneratedInvite);

        viewModel.Deactivate();

        Assert.Empty(viewModel.InviteCode);
        Assert.Empty(viewModel.ExpiresAtText);
        Assert.Empty(viewModel.SuccessMessage);
        Assert.Empty(viewModel.RecipientInstruction);
        Assert.False(viewModel.HasGeneratedInvite);
        Assert.False(viewModel.CanProvision);

        await viewModel.InitializeAsync();
        Assert.Equal(2, identityCalls);
        Assert.True(viewModel.CanProvision);
    }

    [Fact]
    public async Task Deactivate_discards_late_success_from_in_flight_provisioning()
    {
        var responseGate = new TaskCompletionSource<OperationalResourceResult<StaffInviteProvisioningResponseDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CreateStaffInviteDto? submitted = null;
        var api = CreateApi((method, arguments) => method.Name switch
        {
            nameof(IApiService.GetCurrentUserForStartupAsync) => Task.FromResult(OwnerIdentity()),
            nameof(IApiService.ProvisionStaffAsync) => CaptureAndWait((CreateStaffInviteDto)arguments![0]!),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<OperationalResourceResult<StaffInviteProvisioningResponseDto>> CaptureAndWait(
            CreateStaffInviteDto request)
        {
            submitted = request;
            return responseGate.Task;
        }
        var viewModel = ValidViewModel(api);
        await viewModel.InitializeAsync();

        var provision = viewModel.ProvisionStaffAsync();
        viewModel.Deactivate();
        responseGate.SetResult(SuccessfulProvision(submitted!));
        await provision;

        Assert.Empty(viewModel.InviteCode);
        Assert.Empty(viewModel.ExpiresAtText);
        Assert.Empty(viewModel.SuccessMessage);
        Assert.Empty(viewModel.RecipientInstruction);
        Assert.False(viewModel.HasGeneratedInvite);
        Assert.False(viewModel.CanProvision);
    }

    private static StaffProvisioningViewModel ValidViewModel(IApiService api) => new(
        api,
        new RecordingClipboard(),
        new RecordingDialog())
    {
        FirstName = "Ria",
        LastName = "Santos",
        Email = "receptionist@example.com",
        Purpose = "Receptionist mobile app access"
    };

    private static StaffProvisioningViewModel CreateViewModel(IApiService api) => new(
        api,
        new RecordingClipboard(),
        new RecordingDialog());

    private static StartupIdentityLookupResult OwnerIdentity() => new(
        StartupIdentityLookupStatus.Success,
        new UserResponseDto { Role = UserRole.Administrator },
        HttpStatusCode.OK);

    private static OperationalResourceResult<StaffInviteProvisioningResponseDto> SuccessfulProvision(
        CreateStaffInviteDto request) => new(
        OperationalResourceStatus.Success,
        new StaffInviteProvisioningResponseDto
        {
            User = new UserResponseDto
            {
                UserID = 8,
                FirstName = "Ria",
                LastName = "Santos",
                Email = request.Email,
                Role = UserRole.Receptionist
            },
            Invite = new AppInviteCodeResponseDto
            {
                InviteCode = "one-time-code",
                Details = new AppInviteResponseDto
                {
                    IntendedRole = UserRole.Receptionist,
                    ExpiresAtUtc = new DateTime(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc)
                }
            }
        },
        HttpStatusCode.Created);

    private static IApiService CreateApi(
        Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = DispatchProxy.Create<IApiService, ApiProxy>();
        ((ApiProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class ApiProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args);
    }

    private sealed class RecordingClipboard : IAppClipboardService
    {
        public string? CopiedText { get; private set; }

        public Task SetTextAsync(string text)
        {
            CopiedText = text;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDialog : IAppDialogService
    {
        public int AlertCount { get; private set; }

        public Task ShowAlertAsync(string title, string message, string cancel)
        {
            AlertCount++;
            return Task.CompletedTask;
        }

        public Task<bool> ShowConfirmationAsync(
            string title,
            string message,
            string accept,
            string cancel) => Task.FromResult(false);
    }
}
