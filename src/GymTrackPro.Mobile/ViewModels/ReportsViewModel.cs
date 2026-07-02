using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile.ViewModels;

public partial class ReportsViewModel : BaseViewModel
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    private string reportType = "Daily Revenue"; // Daily Revenue, Attendance, Membership Sales, Refunds

    [ObservableProperty]
    private DateTime startDate = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime endDate = DateTime.Today;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private string successMessage = string.Empty;

    public ObservableCollection<ReportItemDto> ReportRows { get; } = new();

    public ReportsViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Reports";
    }

    [RelayCommand]
    public async Task GenerateReportAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;
        ReportRows.Clear();

        try
        {
            if (ReportType == "Daily Revenue")
            {
                var res = await _apiService.GetDailyRevenueReportAsync(StartDate, EndDate);
                if (res.Success && res.Data != null)
                {
                    foreach (var item in res.Data)
                    {
                        ReportRows.Add(new ReportItemDto
                        {
                            Col1 = item.Date.ToString("yyyy-MM-dd"),
                            Col2 = $"Count: {item.TransactionCount}",
                            Col3 = $"Gross: ₱{item.GrossAmount:F2}",
                            Col4 = $"Net: ₱{item.NetAmount:F2}"
                        });
                    }
                }
            }
            else if (ReportType == "Attendance")
            {
                var res = await _apiService.GetAttendanceReportAsync(StartDate, EndDate);
                if (res.Success && res.Data != null)
                {
                    foreach (var item in res.Data)
                    {
                        ReportRows.Add(new ReportItemDto
                        {
                            Col1 = item.MemberName,
                            Col2 = item.PlanName,
                            Col3 = $"In: {item.CheckInTime:HH:mm}",
                            Col4 = item.CheckOutTime.HasValue ? $"Out: {item.CheckOutTime.Value:HH:mm}" : "Active"
                        });
                    }
                }
            }
            else if (ReportType == "Membership Sales")
            {
                var res = await _apiService.GetMembershipSalesReportAsync(StartDate, EndDate);
                if (res.Success && res.Data != null)
                {
                    foreach (var item in res.Data)
                    {
                        ReportRows.Add(new ReportItemDto
                        {
                            Col1 = item.MemberName,
                            Col2 = item.PlanName,
                            Col3 = $"Amount: ₱{item.Amount:F2}",
                            Col4 = $"Paid: ₱{item.FinalAmount:F2}"
                        });
                    }
                }
            }
            else if (ReportType == "Refunds")
            {
                var res = await _apiService.GetRefundReportAsync(StartDate, EndDate);
                if (res.Success && res.Data != null)
                {
                    foreach (var item in res.Data)
                    {
                        ReportRows.Add(new ReportItemDto
                        {
                            Col1 = item.MemberName,
                            Col2 = $"Receipt: {item.ReceiptNumber}",
                            Col3 = $"Refunded: ₱{item.Amount:F2}",
                            Col4 = item.DateRefunded.ToString("yyyy-MM-dd HH:mm")
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to generate report: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            byte[] csvBytes = Array.Empty<byte>();
            string filename = $"{ReportType.Replace(" ", "_").ToLower()}_{StartDate:yyyyMMdd}_{EndDate:yyyyMMdd}.csv";

            if (ReportType == "Daily Revenue")
            {
                csvBytes = await _apiService.ExportDailyRevenueCsvAsync(StartDate, EndDate);
            }
            else if (ReportType == "Attendance")
            {
                csvBytes = await _apiService.ExportAttendanceCsvAsync(StartDate, EndDate);
            }
            else if (ReportType == "Membership Sales")
            {
                csvBytes = await _apiService.ExportMembershipSalesCsvAsync(StartDate, EndDate);
            }
            else if (ReportType == "Refunds")
            {
                csvBytes = await _apiService.ExportRefundsCsvAsync(StartDate, EndDate);
            }

            if (csvBytes == null || csvBytes.Length == 0)
            {
                ErrorMessage = "No data to export.";
                return;
            }

            // Save file in standard local path
            string targetDir = FileSystem.CacheDirectory;
            string targetPath = Path.Combine(targetDir, filename);
            await File.WriteAllBytesAsync(targetPath, csvBytes);

            SuccessMessage = $"Exported successfully to: {targetPath}";
            await Shell.Current.DisplayAlertAsync("Export Success", $"Report exported to {filename} in cache folder.\nPath: {targetPath}", "OK");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public class ReportItemDto
{
    public string Col1 { get; set; } = string.Empty;
    public string Col2 { get; set; } = string.Empty;
    public string Col3 { get; set; } = string.Empty;
    public string Col4 { get; set; } = string.Empty;
}
