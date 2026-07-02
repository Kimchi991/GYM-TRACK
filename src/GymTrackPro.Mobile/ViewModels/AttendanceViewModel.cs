using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class AttendanceViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial string QrCodeInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSuccessMessage { get; set; }

    public ObservableCollection<AttendanceDto> TodayCheckIns { get; } = new();

    public AttendanceViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Attendance";
    }

    [RelayCommand]
    public async Task CheckInAsync()
    {
        if (string.IsNullOrWhiteSpace(QrCodeInput))
        {
            ErrorMessage = "Please scan or enter a QR Code.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        IsSuccessMessage = false;

        try
        {
            var result = await _apiService.CheckInAsync(QrCodeInput);
            if (result.Success && result.Data != null)
            {
                StatusMessage = $"Checked in successfully: {result.Data.MemberName}";
                IsSuccessMessage = true;
                QrCodeInput = string.Empty;
                await LoadTodayCheckInsAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Check-in failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CheckOutAsync(AttendanceDto attendance)
    {
        if (attendance == null) return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        IsSuccessMessage = false;

        try
        {
            var result = await _apiService.CheckOutAsync(attendance.AttendanceID);
            if (result.Success)
            {
                StatusMessage = $"Checked out successfully: {attendance.MemberName}";
                IsSuccessMessage = true;
                await LoadTodayCheckInsAsync();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Check-out failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task LoadTodayCheckInsAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            // Fetch active sessions / check-ins by using daily reports or active logs.
            // Since we can fetch check-ins for the general report, or active logs:
            // Let's call the daily report endpoint for today's date!
            var today = DateTime.Today;
            var result = await _apiService.GetAttendanceReportAsync(today, today.AddDays(1).AddSeconds(-1));
            if (result.Success && result.Data != null)
            {
                TodayCheckIns.Clear();
                foreach (var item in result.Data)
                {
                    TodayCheckIns.Add(new AttendanceDto
                    {
                        AttendanceID = item.AttendanceID,
                        MemberName = item.MemberName,
                        CheckInTime = item.CheckInTime,
                        CheckOutTime = item.CheckOutTime
                    });
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading check-ins: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
