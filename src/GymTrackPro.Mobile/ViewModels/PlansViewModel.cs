using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;
using Microsoft.Maui.Controls;

namespace GymTrackPro.Mobile.ViewModels;

public partial class PlansViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowPlanForm { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial int EditingPlanId { get; set; }

    // Plan form properties
    [ObservableProperty]
    public partial string PlanName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int DurationDays { get; set; } = 30;

    [ObservableProperty]
    public partial decimal Price { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; } = string.Empty;

    public ObservableCollection<MembershipPlanResponseDto> Plans { get; } = new();

    public PlansViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Membership Plans";
    }

    [RelayCommand]
    public async Task LoadPlansAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var result = await _apiService.GetPlansAsync();
            if (result.Success && result.Data != null)
            {
                Plans.Clear();
                foreach (var plan in result.Data)
                {
                    Plans.Add(plan);
                }
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading plans: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void TogglePlanForm()
    {
        ShowPlanForm = !ShowPlanForm;
        if (ShowPlanForm && !IsEditing)
        {
            // Reset fields for new plan
            PlanName = string.Empty;
            DurationDays = 30;
            Price = 0;
            Description = string.Empty;
        }
    }

    [RelayCommand]
    private void OpenEditForm(MembershipPlanResponseDto plan)
    {
        if (plan == null) return;

        IsEditing = true;
        EditingPlanId = plan.PlanID;
        PlanName = plan.PlanName;
        DurationDays = plan.DurationDays;
        Price = plan.Price;
        Description = plan.Description;
        ShowPlanForm = true;
    }

    [RelayCommand]
    private async Task SavePlanAsync()
    {
        if (string.IsNullOrWhiteSpace(PlanName) || DurationDays <= 0 || Price <= 0)
        {
            ErrorMessage = "Plan Name, valid Duration Days, and valid Price are required.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var dto = new CreateMembershipPlanDto
            {
                PlanName = PlanName,
                DurationDays = DurationDays,
                Price = Price,
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description
            };

            if (IsEditing)
            {
                var result = await _apiService.UpdatePlanAsync(EditingPlanId, dto);
                if (result.Success)
                {
                    SuccessMessage = $"Plan '{PlanName}' updated successfully.";
                    ShowPlanForm = false;
                    IsEditing = false;
                    await LoadPlansAsync();
                }
                else
                {
                    ErrorMessage = result.Message;
                }
            }
            else
            {
                var result = await _apiService.CreatePlanAsync(dto);
                if (result.Success)
                {
                    SuccessMessage = $"Plan '{PlanName}' created successfully.";
                    ShowPlanForm = false;
                    await LoadPlansAsync();
                }
                else
                {
                    ErrorMessage = result.Message;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save plan: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeletePlanAsync(MembershipPlanResponseDto plan)
    {
        if (plan == null) return;

        bool confirm = await Shell.Current.DisplayAlertAsync("Confirm Delete", $"Are you sure you want to delete membership plan '{plan.PlanName}'?", "Yes", "No");
        if (!confirm) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var result = await _apiService.DeletePlanAsync(plan.PlanID);
            if (result.Success)
            {
                SuccessMessage = "Plan deleted successfully.";
                await LoadPlansAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete plan: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
