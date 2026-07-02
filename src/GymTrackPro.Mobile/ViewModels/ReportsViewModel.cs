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
    public partial string ReportType { get; set; } = "Daily Revenue"; // Daily Revenue, Monthly Revenue, Attendance, Membership Sales, Expiring Memberships, Refunds

    [ObservableProperty]
    public partial DateTime StartDate { get; set; } = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    public partial DateTime EndDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Header1 { get; set; } = "Date";

    [ObservableProperty]
    public partial string Header2 { get; set; } = "Transactions";

    [ObservableProperty]
    public partial string Header3 { get; set; } = "Gross Amount";

    [ObservableProperty]
    public partial string Header4 { get; set; } = "Net Amount";

    public ObservableCollection<ReportItemDto> ReportRows { get; } = new();

    public ReportsViewModel(IApiService apiService)
    {
        _apiService = apiService;
        Title = "Reports";
        UpdateHeaders();
    }

    partial void OnReportTypeChanged(string value)
    {
        UpdateHeaders();
    }

    private void UpdateHeaders()
    {
        switch (ReportType)
        {
            case "Daily Revenue":
                Header1 = "Date";
                Header2 = "Transactions";
                Header3 = "Gross Amount";
                Header4 = "Net Amount";
                break;
            case "Monthly Revenue":
                Header1 = "Month";
                Header2 = "Transactions";
                Header3 = "Gross Amount";
                Header4 = "Net Amount";
                break;
            case "Attendance":
                Header1 = "Member";
                Header2 = "Plan Package";
                Header3 = "Checked-In";
                Header4 = "Checked-Out";
                break;
            case "Membership Sales":
                Header1 = "Member";
                Header2 = "Plan Package";
                Header3 = "Base Price";
                Header4 = "Amount Paid";
                break;
            case "Expiring Memberships":
                Header1 = "Member";
                Header2 = "Plan Package";
                Header3 = "Start Date";
                Header4 = "Expiry Date";
                break;
            case "Refunds":
                Header1 = "Member";
                Header2 = "Receipt #";
                Header3 = "Refund Amt";
                Header4 = "Refund Date";
                break;
            default:
                Header1 = "Col 1";
                Header2 = "Col 2";
                Header3 = "Col 3";
                Header4 = "Col 4";
                break;
        }
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
            else if (ReportType == "Monthly Revenue")
            {
                var res = await _apiService.GetMonthlyRevenueReportAsync(StartDate, EndDate);
                if (res.Success && res.Data != null)
                {
                    foreach (var item in res.Data)
                    {
                        ReportRows.Add(new ReportItemDto
                        {
                            Col1 = item.Month,
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
            else if (ReportType == "Expiring Memberships")
            {
                int days = (int)(EndDate - StartDate).TotalDays;
                if (days <= 0) days = 7;
                var res = await _apiService.GetExpiringMembershipsReportAsync(days);
                if (res.Success && res.Data != null)
                {
                    foreach (var item in res.Data)
                    {
                        ReportRows.Add(new ReportItemDto
                        {
                            Col1 = item.MemberName,
                            Col2 = item.PlanName,
                            Col3 = item.StartDate.ToString("yyyy-MM-dd"),
                            Col4 = item.EndDate.ToString("yyyy-MM-dd")
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
            else if (ReportType == "Monthly Revenue")
            {
                csvBytes = await _apiService.ExportMonthlyRevenueCsvAsync(StartDate, EndDate);
            }
            else if (ReportType == "Attendance")
            {
                csvBytes = await _apiService.ExportAttendanceCsvAsync(StartDate, EndDate);
            }
            else if (ReportType == "Membership Sales")
            {
                csvBytes = await _apiService.ExportMembershipSalesCsvAsync(StartDate, EndDate);
            }
            else if (ReportType == "Expiring Memberships")
            {
                int days = (int)(EndDate - StartDate).TotalDays;
                if (days <= 0) days = 7;
                csvBytes = await _apiService.ExportExpiringMembershipsCsvAsync(days);
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
