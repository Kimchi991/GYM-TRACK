using System.ComponentModel.DataAnnotations;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.Mobile.ViewModels;

public partial class StaffProvisioningViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private readonly IAppClipboardService _clipboardService;
    private readonly IAppDialogService _dialogService;
    private bool _initialized;
    private CancellationTokenSource? _authorizationCancellation;
    private int _authorizationGeneration;
    private CancellationTokenSource? _provisioningCancellation;
    private int _provisioningGeneration;

    [ObservableProperty]
    public partial string FirstName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Purpose { get; set; } = "Receptionist mobile app access";

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string InviteCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExpiresAtText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RecipientInstruction { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanProvision { get; set; }

    [ObservableProperty]
    public partial bool HasGeneratedInvite { get; set; }

    public StaffProvisioningViewModel(
        IApiService apiService,
        IAppClipboardService clipboardService,
        IAppDialogService dialogService)
    {
        _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
        _clipboardService = clipboardService
            ?? throw new ArgumentNullException(nameof(clipboardService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        Title = "Add Receptionist";
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (_initialized || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        _authorizationCancellation?.Cancel();
        _authorizationCancellation?.Dispose();
        _authorizationCancellation = new CancellationTokenSource();
        var cancellationToken = _authorizationCancellation.Token;
        var authorizationGeneration = Interlocked.Increment(ref _authorizationGeneration);
        try
        {
            var identity = await _apiService.GetCurrentUserForStartupAsync(cancellationToken);
            if (authorizationGeneration != Volatile.Read(ref _authorizationGeneration))
            {
                return;
            }
            if (identity.Status == StartupIdentityLookupStatus.Success
                && identity.User?.Role == UserRole.Administrator)
            {
                CanProvision = true;
                _initialized = true;
                return;
            }

            CanProvision = false;
            ErrorMessage = identity.Status switch
            {
                StartupIdentityLookupStatus.Unavailable =>
                    "Owner access could not be verified because the server is unavailable. Check the connection and reopen this page.",
                _ when identity.HttpStatusCode == HttpStatusCode.Unauthorized =>
                    "Your session has expired. Sign in again before creating staff invites.",
                _ => "Only the gym owner can create Receptionist profiles and invites."
            };
            _initialized = identity.Status != StartupIdentityLookupStatus.Unavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page left the visual tree or a newer authorization check replaced this one.
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException)
        {
            CanProvision = false;
            ErrorMessage = "Owner access could not be verified because the server is unavailable. Check the connection and reopen this page.";
        }
        catch (Exception)
        {
            CanProvision = false;
            _initialized = false;
            ErrorMessage = "Owner access could not be verified. Sign in again if the problem continues; staff provisioning remains locked.";
        }
        finally
        {
            if (authorizationGeneration == Volatile.Read(ref _authorizationGeneration))
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    public async Task ProvisionStaffAsync()
    {
        if (IsBusy || !CanProvision)
        {
            return;
        }

        ErrorMessage = ValidateForm();
        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            return;
        }

        IsBusy = true;
        SuccessMessage = string.Empty;
        HasGeneratedInvite = false;
        _provisioningCancellation?.Cancel();
        _provisioningCancellation?.Dispose();
        _provisioningCancellation = new CancellationTokenSource();
        var cancellationToken = _provisioningCancellation.Token;
        var provisioningGeneration = Interlocked.Increment(ref _provisioningGeneration);
        try
        {
            if (!StaffInviteEmailCanonicalizer.TryCanonicalize(
                    Email,
                    out var canonicalEmail,
                    out _))
            {
                ErrorMessage = "Enter a valid recipient email address.";
                return;
            }

            var request = new CreateStaffInviteDto
            {
                FirstName = FirstName.Trim(),
                LastName = LastName.Trim(),
                Email = canonicalEmail,
                Purpose = Purpose.Trim()
            };
            var result = await _apiService.ProvisionStaffAsync(request, cancellationToken);
            if (provisioningGeneration != Volatile.Read(ref _provisioningGeneration))
            {
                return;
            }
            if (result.Status == OperationalResourceStatus.Success
                && result.Data is not null
                && IsValidStaffResponse(result.Data, request.Email))
            {
                InviteCode = result.Data.Invite.InviteCode;
                var expiresAtUtc = result.Data.Invite.Details.ExpiresAtUtc.ToUniversalTime();
                ExpiresAtText = $"Expires: {expiresAtUtc:yyyy-MM-dd HH:mm} UTC";
                RecipientInstruction =
                    $"Send this code to {request.Email}. They must register and verify that same email, then enter the code on Activate Account.";
                SuccessMessage =
                    $"Receptionist profile created for {result.Data.User.FirstName} {result.Data.User.LastName}. The invite is shown only in this response.";
                HasGeneratedInvite = true;
                CanProvision = false;
                return;
            }

            if (result.Status == OperationalResourceStatus.Success)
            {
                ErrorMessage = "The server returned an incomplete Receptionist invite. Do not share it; refresh and verify the staff record before retrying.";
                return;
            }

            if (result.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                CanProvision = false;
            }
            ErrorMessage = FormatFailure(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leaving the page cancels presentation of any late response.
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException)
        {
            ErrorMessage = "The server could not be reached. No confirmed staff invite was created; check the connection before retrying.";
        }
        catch (Exception)
        {
            ErrorMessage = "Staff provisioning failed unexpectedly. No invite is confirmed; verify the staff list before retrying.";
        }
        finally
        {
            if (provisioningGeneration == Volatile.Read(ref _provisioningGeneration))
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    public async Task CopyInviteAsync()
    {
        if (!HasGeneratedInvite || string.IsNullOrWhiteSpace(InviteCode))
        {
            return;
        }

        try
        {
            await _clipboardService.SetTextAsync(InviteCode);
            await _dialogService.ShowAlertAsync(
                "Invite Copied",
                "The one-time invite code was copied. Share it only with the intended Receptionist.",
                "OK");
        }
        catch (Exception)
        {
            ErrorMessage = "The invite could not be copied. Select the visible code and share it securely with the intended Receptionist.";
        }
    }

    public void Deactivate()
    {
        _authorizationCancellation?.Cancel();
        _authorizationCancellation?.Dispose();
        _authorizationCancellation = null;
        Interlocked.Increment(ref _authorizationGeneration);
        _provisioningCancellation?.Cancel();
        _provisioningCancellation?.Dispose();
        _provisioningCancellation = null;
        Interlocked.Increment(ref _provisioningGeneration);
        _initialized = false;
        CanProvision = false;
        IsBusy = false;
        ClearInvitePresentation();
        ErrorMessage = string.Empty;
    }

    private string ValidateForm()
    {
        if (string.IsNullOrWhiteSpace(FirstName)
            || string.IsNullOrWhiteSpace(LastName)
            || string.IsNullOrWhiteSpace(Email)
            || string.IsNullOrWhiteSpace(Purpose))
        {
            return "First name, last name, recipient email, and invite purpose are required.";
        }

        if (FirstName.Trim().Length > 100
            || LastName.Trim().Length > 100
            || Purpose.Trim().Length > 100)
        {
            return "Names and invite purpose must each be 100 characters or fewer.";
        }

        if (!StaffInviteEmailCanonicalizer.TryCanonicalize(
                Email,
                out var canonicalEmail,
                out _))
        {
            return "Enter a valid recipient email address.";
        }

        return new EmailAddressAttribute().IsValid(canonicalEmail)
            ? string.Empty
            : "Enter a valid recipient email address.";
    }

    private static string FormatFailure(
        OperationalResourceResult<StaffInviteProvisioningResponseDto> result)
    {
        if (result.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            return "Your session has expired. Sign in again before creating staff invites.";
        }

        if (result.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            return "Only the gym owner can create Receptionist profiles and invites.";
        }

        if (result.HttpStatusCode == HttpStatusCode.Conflict
            || string.Equals(result.ErrorCode, "IDENTITY_CONFLICT", StringComparison.Ordinal))
        {
            return "That email is already linked to an application identity. Confirm the recipient email or use the existing account.";
        }

        if (result.Status == OperationalResourceStatus.Unavailable)
        {
            return "The server could not be reached. No confirmed staff invite was created; check the connection before retrying.";
        }

        return string.IsNullOrWhiteSpace(result.Message)
            ? "The Receptionist invite could not be created. Review the details and try again."
            : result.Message;
    }

    private static bool IsValidStaffResponse(
        StaffInviteProvisioningResponseDto response,
        string expectedEmail)
    {
        if (!StaffInviteEmailCanonicalizer.TryCanonicalize(
                expectedEmail,
                out _,
                out var expectedNormalizedEmail)
            || !StaffInviteEmailCanonicalizer.TryCanonicalize(
                response.User.Email,
                out _,
                out var responseNormalizedEmail))
        {
            return false;
        }

        return response.User.UserID > 0
            && response.User.Role == UserRole.Receptionist
            && string.Equals(
                responseNormalizedEmail,
                expectedNormalizedEmail,
                StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(response.Invite.InviteCode)
            && response.Invite.Details.IntendedRole == UserRole.Receptionist
            && response.Invite.Details.ExpiresAtUtc != default;
    }

    private void ClearInvitePresentation()
    {
        InviteCode = string.Empty;
        ExpiresAtText = string.Empty;
        SuccessMessage = string.Empty;
        RecipientInstruction = string.Empty;
        HasGeneratedInvite = false;
    }
}
