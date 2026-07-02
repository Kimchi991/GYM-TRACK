using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class MembersViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public ObservableCollection<MemberResponseDto> Members { get; } = new();

    // Fields for Registration Form
    [ObservableProperty]
    public partial string RegFirstName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegLastName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegGender { get; set; } = "Male";

    [ObservableProperty]
    public partial DateTime RegBirthDate { get; set; } = DateTime.Now.AddYears(-20);

    [ObservableProperty]
    public partial string RegPhone { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegEmail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegAddress { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegEmergencyContact { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RegPhotoBase64 { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowAddForm { get; set; }

    [ObservableProperty]
    public partial MemberResponseDto? SelectedMember { get; set; }

    public MembersViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Members";
    }

    [RelayCommand]
    public async Task LoadMembersAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _apiService.GetMembersAsync(SearchQuery);
            if (result.Success && result.Data != null)
            {
                Members.Clear();
                foreach (var member in result.Data.Items)
                {
                    Members.Add(member);
                }
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading members: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleAddForm()
    {
        ShowAddForm = !ShowAddForm;
        if (ShowAddForm)
        {
            // Reset fields
            RegFirstName = string.Empty;
            RegLastName = string.Empty;
            RegPhone = string.Empty;
            RegEmail = string.Empty;
            RegAddress = string.Empty;
            RegEmergencyContact = string.Empty;
            RegPhotoBase64 = string.Empty;
        }
    }

    [RelayCommand]
    private async Task RegisterMemberAsync()
    {
        if (string.IsNullOrWhiteSpace(RegFirstName) || string.IsNullOrWhiteSpace(RegLastName) ||
            string.IsNullOrWhiteSpace(RegPhone) || string.IsNullOrWhiteSpace(RegEmergencyContact))
        {
            ErrorMessage = "First Name, Last Name, Phone, and Emergency Contact are required.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var dto = new CreateMemberDto
            {
                FirstName = RegFirstName,
                LastName = RegLastName,
                Gender = RegGender,
                BirthDate = RegBirthDate,
                PhoneNumber = RegPhone,
                Email = string.IsNullOrWhiteSpace(RegEmail) ? null : RegEmail,
                Address = string.IsNullOrWhiteSpace(RegAddress) ? null : RegAddress,
                EmergencyContact = RegEmergencyContact,
                ProfilePictureBase64 = string.IsNullOrWhiteSpace(RegPhotoBase64) ? null : RegPhotoBase64
            };

            var result = await _apiService.CreateMemberAsync(dto);
            if (result.Success)
            {
                ShowAddForm = false;
                await LoadMembersAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Registration failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GoToDetailsAsync(MemberResponseDto member)
    {
        if (member == null) return;
        await Shell.Current.GoToAsync("memberdetails", new Dictionary<string, object>
        {
            { "Member", member }
        });
    }

    [RelayCommand]
    private async Task DeleteMemberAsync(MemberResponseDto member)
    {
        if (member == null) return;

        bool confirm = await Shell.Current.DisplayAlertAsync("Confirm Delete", $"Are you sure you want to delete member {member.FirstName} {member.LastName}?", "Yes", "No");
        if (!confirm) return;

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _apiService.DeleteMemberAsync(member.MemberID);
            if (result.Success)
            {
                await LoadMembersAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete member: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
