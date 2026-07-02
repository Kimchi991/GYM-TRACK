using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;
using Microsoft.Maui.Controls;

namespace GymTrackPro.Mobile.ViewModels;

[QueryProperty(nameof(Member), "Member")]
public partial class MemberDetailsViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial MemberResponseDto? Member { get; set; }

    [ObservableProperty]
    public partial SubscriptionResponseDto? ActiveSubscription { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    // Edit Form properties
    [ObservableProperty]
    public partial bool ShowEditForm { get; set; }

    [ObservableProperty]
    public partial string EditFirstName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditLastName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditGender { get; set; } = "Male";

    [ObservableProperty]
    public partial DateTime EditBirthDate { get; set; } = DateTime.Now.AddYears(-20);

    [ObservableProperty]
    public partial string EditPhoneNumber { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? EditEmail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? EditAddress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditEmergencyContact { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EditStatus { get; set; } = "Active";

    [ObservableProperty]
    public partial bool ShowRenewForm { get; set; }

    [ObservableProperty]
    public partial MembershipPlanResponseDto? SelectedPlanForRenewal { get; set; }

    [ObservableProperty]
    public partial decimal RenewalDiscount { get; set; }

    [ObservableProperty]
    public partial string RenewalMethod { get; set; } = "Cash";

    public ObservableCollection<MembershipPlanResponseDto> AvailablePlans { get; } = new();

    public ObservableCollection<SubscriptionResponseDto> Subscriptions { get; } = new();
    public ObservableCollection<AttendanceDto> AttendanceLogs { get; } = new();
    public ObservableCollection<PaymentResponseDto> PaymentLogs { get; } = new();

    public MemberDetailsViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Member Details";
    }

    partial void OnMemberChanged(MemberResponseDto? value)
    {
        if (value != null)
        {
            Task.Run(async () => await LoadMemberDetailsDataAsync());
        }
    }

    public async Task LoadMemberDetailsDataAsync()
    {
        if (Member == null) return;

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            // 1. Fetch Subscriptions
            var subRes = await _apiService.GetSubscriptionsByMemberIdAsync(Member.MemberID);
            if (subRes.Success && subRes.Data != null)
            {
                Subscriptions.Clear();
                foreach (var sub in subRes.Data)
                {
                    Subscriptions.Add(sub);
                }
                // Find Active or Paused subscription
                ActiveSubscription = Subscriptions.FirstOrDefault(s => s.Status == "Active" || s.Status == "Paused");
            }

            // 2. Fetch Attendance
            var attRes = await _apiService.GetAttendanceByMemberIdAsync(Member.MemberID);
            if (attRes.Success && attRes.Data != null)
            {
                AttendanceLogs.Clear();
                foreach (var log in attRes.Data.OrderByDescending(a => a.CheckInTime))
                {
                    AttendanceLogs.Add(log);
                }
            }

            // 3. Fetch Payments
            var payRes = await _apiService.GetPaymentsAsync(Member.MemberID);
            if (payRes.Success && payRes.Data != null)
            {
                PaymentLogs.Clear();
                foreach (var pay in payRes.Data.OrderByDescending(p => p.DatePaid))
                {
                    PaymentLogs.Add(pay);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading details: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleEditForm()
    {
        if (Member == null) return;

        ShowEditForm = !ShowEditForm;
        if (ShowEditForm)
        {
            EditFirstName = Member.FirstName;
            EditLastName = Member.LastName;
            EditGender = Member.Gender;
            EditBirthDate = Member.BirthDate;
            EditPhoneNumber = Member.PhoneNumber;
            EditEmail = Member.Email;
            EditAddress = Member.Address;
            EditEmergencyContact = Member.EmergencyContact;
            EditStatus = Member.Status;
        }
    }

    [RelayCommand]
    private async Task SaveMemberAsync()
    {
        if (Member == null) return;

        if (string.IsNullOrWhiteSpace(EditFirstName) || string.IsNullOrWhiteSpace(EditLastName) ||
            string.IsNullOrWhiteSpace(EditPhoneNumber) || string.IsNullOrWhiteSpace(EditEmergencyContact))
        {
            ErrorMessage = "First Name, Last Name, Phone, and Emergency Contact are required.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var dto = new UpdateMemberDto
            {
                FirstName = EditFirstName,
                LastName = EditLastName,
                Gender = EditGender,
                BirthDate = EditBirthDate,
                PhoneNumber = EditPhoneNumber,
                Email = string.IsNullOrWhiteSpace(EditEmail) ? null : EditEmail,
                Address = string.IsNullOrWhiteSpace(EditAddress) ? null : EditAddress,
                EmergencyContact = EditEmergencyContact,
                Status = EditStatus
            };

            var result = await _apiService.UpdateMemberAsync(Member.MemberID, dto);
            if (result.Success && result.Data != null)
            {
                Member = result.Data;
                ShowEditForm = false;
                SuccessMessage = "Member updated successfully.";
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PauseSubscriptionAsync()
    {
        if (ActiveSubscription == null) return;

        string reason = await Shell.Current.DisplayPromptAsync("Pause Membership", "Enter reason for pausing membership:", "OK", "Cancel", "Medical / Vacation / Personal");
        if (string.IsNullOrWhiteSpace(reason)) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var pauseDto = new PauseSubscriptionDto { Reason = reason };
            var result = await _apiService.PauseSubscriptionAsync(ActiveSubscription.SubscriptionID, pauseDto);
            if (result.Success)
            {
                SuccessMessage = "Subscription paused successfully.";
                await LoadMemberDetailsDataAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to pause: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResumeSubscriptionAsync()
    {
        if (ActiveSubscription == null) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var result = await _apiService.ResumeSubscriptionAsync(ActiveSubscription.SubscriptionID);
            if (result.Success)
            {
                SuccessMessage = "Subscription resumed successfully.";
                await LoadMemberDetailsDataAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to resume: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task DeleteMemberAsync()
    {
        if (Member == null) return;

        bool confirm = await Shell.Current.DisplayAlertAsync("Confirm Delete", $"Are you sure you want to delete Member {Member.FirstName} {Member.LastName}?", "Yes", "No");
        if (!confirm) return;

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _apiService.DeleteMemberAsync(Member.MemberID);
            if (result.Success)
            {
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete Member: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleRenewFormAsync()
    {
        ShowRenewForm = !ShowRenewForm;
        if (ShowRenewForm)
        {
            SelectedPlanForRenewal = null;
            RenewalDiscount = 0;
            RenewalMethod = "Cash";
            AvailablePlans.Clear();
            
            IsBusy = true;
            ErrorMessage = string.Empty;
            try
            {
                var result = await _apiService.GetPlansAsync();
                if (result.Success && result.Data != null)
                {
                    foreach (var plan in result.Data)
                    {
                        AvailablePlans.Add(plan);
                    }
                }
                else
                {
                    ErrorMessage = result.Message;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load plans: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task ProcessRenewalAsync()
    {
        if (Member == null || SelectedPlanForRenewal == null)
        {
            ErrorMessage = "Please select a membership plan.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            // 1. Create Subscription
            var subDto = new CreateSubscriptionDto
            {
                MemberID = Member.MemberID,
                PlanID = SelectedPlanForRenewal.PlanID,
                StartDate = DateTime.Today
            };

            var subResult = await _apiService.CreateSubscriptionAsync(subDto);
            if (!subResult.Success || subResult.Data == null)
            {
                ErrorMessage = subResult.Message;
                return;
            }

            // 2. Process Payment
            var payDto = new CreatePaymentDto
            {
                MemberID = Member.MemberID,
                SubscriptionID = subResult.Data.SubscriptionID,
                Amount = SelectedPlanForRenewal.Price,
                Discount = RenewalDiscount,
                PaymentMethod = RenewalMethod,
                PaymentStatus = "Paid"
            };

            var payResult = await _apiService.ProcessPaymentAsync(payDto);
            if (payResult.Success && payResult.Data != null)
            {
                SuccessMessage = $"Renewal Successful! Receipt {payResult.Data.ReceiptNumber} generated.";
                ShowRenewForm = false;
                await LoadMemberDetailsDataAsync();
            }
            else
            {
                ErrorMessage = payResult.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Renewal failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

