using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class PaymentsViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    public ObservableCollection<PaymentResponseDto> Payments { get; } = new();
    public ObservableCollection<MemberResponseDto> Members { get; } = new();
    public ObservableCollection<MembershipPlanResponseDto> Plans { get; } = new();

    // New Payment Billing Form Fields
    [ObservableProperty]
    public partial MemberResponseDto? SelectedMemberForPayment { get; set; }

    [ObservableProperty]
    public partial MembershipPlanResponseDto? SelectedPlanForPayment { get; set; }

    [ObservableProperty]
    public partial decimal PaymentAmount { get; set; }

    [ObservableProperty]
    public partial decimal PaymentDiscount { get; set; }

    [ObservableProperty]
    public partial string PaymentMethod { get; set; } = "Cash";

    [ObservableProperty]
    public partial bool ShowBillingForm { get; set; }

    public PaymentsViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Payments";
    }

    [RelayCommand]
    public async Task LoadPaymentsAndDataAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            // Load Payments
            var paymentsResult = await _apiService.GetPaymentsAsync();
            if (paymentsResult.Success && paymentsResult.Data != null)
            {
                Payments.Clear();
                foreach (var p in paymentsResult.Data)
                {
                    Payments.Add(p);
                }
            }

            // Load Members for Billing selector
            var membersResult = await _apiService.GetMembersAsync(pageSize: 100);
            if (membersResult.Success && membersResult.Data != null)
            {
                Members.Clear();
                foreach (var m in membersResult.Data.Items)
                {
                    Members.Add(m);
                }
            }

            // Load Plans
            var plansResult = await _apiService.GetPlansAsync();
            if (plansResult.Success && plansResult.Data != null)
            {
                Plans.Clear();
                foreach (var pl in plansResult.Data)
                {
                    Plans.Add(pl);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading payments data: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleBillingForm()
    {
        ShowBillingForm = !ShowBillingForm;
        if (ShowBillingForm)
        {
            SelectedMemberForPayment = null;
            SelectedPlanForPayment = null;
            PaymentAmount = 0;
            PaymentDiscount = 0;
            PaymentMethod = "Cash";
        }
    }

    partial void OnSelectedPlanForPaymentChanged(MembershipPlanResponseDto? value)
    {
        if (value != null)
        {
            PaymentAmount = value.Price;
        }
    }

    [RelayCommand]
    private async Task ProcessBillingAsync()
    {
        if (SelectedMemberForPayment == null || SelectedPlanForPayment == null)
        {
            ErrorMessage = "Please select both a member and a membership plan.";
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
                MemberID = SelectedMemberForPayment.MemberID,
                PlanID = SelectedPlanForPayment.PlanID
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
                MemberID = SelectedMemberForPayment.MemberID,
                SubscriptionID = subResult.Data.SubscriptionID,
                Amount = PaymentAmount,
                Discount = PaymentDiscount,
                PaymentMethod = PaymentMethod
            };

            var payResult = await _apiService.ProcessPaymentAsync(payDto);
            if (payResult.Success && payResult.Data != null)
            {
                SuccessMessage = $"Receipt Generated: {payResult.Data.ReceiptNumber}. Membership Activated!";
                ShowBillingForm = false;
                await LoadPaymentsAndDataAsync();
            }
            else
            {
                ErrorMessage = payResult.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Billing failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefundPaymentAsync(PaymentResponseDto payment)
    {
        if (payment == null) return;

        bool confirm = await Shell.Current.DisplayAlertAsync("Confirm Refund", $"Process refund for receipt {payment.ReceiptNumber}?", "Yes", "No");
        if (!confirm) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var result = await _apiService.RefundPaymentAsync(payment.PaymentID);
            if (result.Success)
            {
                SuccessMessage = "Payment refunded successfully.";
                await LoadPaymentsAndDataAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Refund failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
