using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;
using GymTrackPro.Shared.Enums;
using Microsoft.Maui.Controls;

namespace GymTrackPro.Mobile.ViewModels;

public partial class ApplicationsViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    public ObservableCollection<ApplicationListItemDto> Applications { get; } = new();

    public ApplicationsViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Registration Approvals";
    }

    [RelayCommand]
    public async Task LoadApplicationsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var result = await _apiService.GetPendingApplicationsAsync();
            if (result.Success && result.Data != null)
            {
                Applications.Clear();
                foreach (var app in result.Data)
                {
                    Applications.Add(app);
                }
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading applications: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApproveApplicationAsync(ApplicationListItemDto app)
    {
        if (app == null) return;

        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Confirm Approval",
            $"Are you sure you want to approve registration for '{app.FullName}' under the {app.SelectedPlanName}?\nPayment Method: {app.PaymentMethod}\nRef: {app.PaymentReferenceNumber ?? "N/A"}",
            "Yes, Approve",
            "Cancel");

        if (!confirm) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var dto = new VerifyApplicationDto
            {
                Status = ApplicationStatus.Approved
            };

            var result = await _apiService.VerifyApplicationAsync(app.ApplicationID, dto);
            if (result.Success)
            {
                SuccessMessage = $"Application for {app.FullName} has been approved. Email notification sent.";
                await LoadApplicationsAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to approve application: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RejectApplicationAsync(ApplicationListItemDto app)
    {
        if (app == null) return;

        string reason = await Shell.Current.DisplayPromptAsync(
            "Reject Application",
            $"Enter the reason for rejecting registration of '{app.FullName}':",
            "Submit Rejection",
            "Cancel",
            "Payment reference not found");

        if (string.IsNullOrWhiteSpace(reason)) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var dto = new VerifyApplicationDto
            {
                Status = ApplicationStatus.Rejected,
                RejectionReason = reason
            };

            var result = await _apiService.VerifyApplicationAsync(app.ApplicationID, dto);
            if (result.Success)
            {
                SuccessMessage = $"Application for {app.FullName} was rejected.";
                await LoadApplicationsAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to reject application: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
