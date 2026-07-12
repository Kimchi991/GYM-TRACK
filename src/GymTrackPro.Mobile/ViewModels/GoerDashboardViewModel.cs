using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class GoerDashboardViewModel : BaseViewModel
{
    private readonly IApiService _apiService;
    private readonly IFirebaseAuthService _authService;
    private readonly IAppLogoutService _logoutService;
    private readonly ILocalDatabaseService _databaseService;
    private readonly ISyncService _syncService;
    private readonly INetworkService _networkService;
    private readonly Func<AppShell> _appShellFactory;
    private readonly IRootNavigationService _rootNavigationService;
    private int _sessionInvalidationStarted;

    [ObservableProperty]
    public partial GoerDashboardDto DashboardData { get; set; } = new();

    [ObservableProperty]
    public partial bool IsAttendanceLoading { get; set; }

    [ObservableProperty]
    public partial bool IsAttendanceEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string AttendanceStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AttendanceErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ImageSource? ProfilePictureSource { get; set; }

    [ObservableProperty]
    public partial bool HasProfilePicture { get; set; }

    [ObservableProperty]
    public partial bool ShowDefaultProfilePicture { get; set; } = true;

    public ObservableCollection<AttendanceDto> RecentAttendance { get; } = new();

    public GoerDashboardViewModel(
        IApiService apiService,
        IFirebaseAuthService authService,
        IAppLogoutService logoutService,
        ILocalDatabaseService databaseService,
        ISyncService syncService,
        INetworkService networkService,
        Func<AppShell> appShellFactory,
        IRootNavigationService rootNavigationService)
    {
        _apiService = apiService;
        _authService = authService;
        _logoutService = logoutService;
        _databaseService = databaseService;
        _syncService = syncService;
        _networkService = networkService;
        _appShellFactory = appShellFactory ?? throw new ArgumentNullException(nameof(appShellFactory));
        _rootNavigationService = rootNavigationService
            ?? throw new ArgumentNullException(nameof(rootNavigationService));
        Title = "My Dashboard";
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        if (IsBusy || IsSessionRejected)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await RefreshCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _logoutService.LogoutAsync();
        if (!_rootNavigationService.TrySetRoot(_appShellFactory()))
        {
            AttendanceErrorMessage = "Signed out, but the login screen could not be displayed.";
        }
    }

    [RelayCommand]
    private async Task CheckInAsync()
    {
        if (IsBusy || IsSessionRejected)
        {
            return;
        }

        IsBusy = true;
        AttendanceErrorMessage = string.Empty;
        try
        {
            var accountUid = RequireAccountUid();
            if (DashboardData.CurrentSession is not null)
            {
                AttendanceErrorMessage = "You already have an active attendance session.";
                return;
            }

            var operationId = Guid.NewGuid();
            if (_networkService.IsConnected)
            {
                var response = await _apiService.GoerCheckInAsync(operationId);
                if (!response.Success)
                {
                    var failureMessage = response.Message;
                    await RefreshCoreAsync();
                    if (!IsSessionRejected)
                    {
                        AttendanceErrorMessage = failureMessage;
                    }
                    return;
                }

                await RefreshCoreAsync();
                AttendanceStatusMessage = "Checked in successfully.";
                return;
            }

            await _syncService.QueueAttendanceOperationAsync(
                accountUid,
                AttendanceSyncAction.CheckIn,
                operationId);
            DashboardData.CurrentSession = new AttendanceDto
            {
                AttendanceDate = DateOnly.FromDateTime(DateTime.Now),
                CheckInTime = DateTime.UtcNow,
                Source = "OfflinePending"
            };
            OnPropertyChanged(nameof(DashboardData));
            AttendanceStatusMessage = "Check-in queued. It will be confirmed when the app reconnects.";
            await SaveCurrentCacheAsync(accountUid);
        }
        catch (Exception exception)
        {
            AttendanceErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckOutAsync()
    {
        if (IsBusy || IsSessionRejected)
        {
            return;
        }

        IsBusy = true;
        AttendanceErrorMessage = string.Empty;
        try
        {
            var accountUid = RequireAccountUid();
            var currentSession = DashboardData.CurrentSession;
            if (currentSession is null)
            {
                AttendanceErrorMessage = "There is no active attendance session to check out.";
                return;
            }

            var operationId = Guid.NewGuid();
            if (_networkService.IsConnected)
            {
                var response = await _apiService.GoerCheckOutAsync(operationId);
                if (!response.Success)
                {
                    var failureMessage = response.Message;
                    await RefreshCoreAsync();
                    if (!IsSessionRejected)
                    {
                        AttendanceErrorMessage = failureMessage;
                    }
                    return;
                }

                await RefreshCoreAsync();
                AttendanceStatusMessage = "Checked out successfully.";
                return;
            }

            await _syncService.QueueAttendanceOperationAsync(
                accountUid,
                AttendanceSyncAction.CheckOut,
                operationId);
            currentSession.CheckOutTime = DateTime.UtcNow;
            currentSession.Source = "OfflinePending";
            RecentAttendance.Insert(0, currentSession);
            DashboardData.CurrentSession = null;
            OnPropertyChanged(nameof(DashboardData));
            IsAttendanceEmpty = RecentAttendance.Count == 0;
            AttendanceStatusMessage = "Check-out queued. It will be confirmed when the app reconnects.";
            await SaveCurrentCacheAsync(accountUid);
        }
        catch (Exception exception)
        {
            AttendanceErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshCoreAsync()
    {
        var accountUid = RequireAccountUid();
        AttendanceErrorMessage = string.Empty;
        IsAttendanceLoading = true;

        try
        {
            await _databaseService.InitializeAsync();
            if (!_networkService.IsConnected)
            {
                await LoadCachedAsync(accountUid, "Offline: showing the most recently cached dashboard.");
                return;
            }

            // Establish active SQL application identity before dispatching any queued
            // operation. The displayed state is fetched again after sync so an
            // acknowledged reconnect check-in/out is visible immediately.
            var authorizationResponse = await _apiService.GetGoerDashboardForRefreshAsync();
            if (await HandleOperationalFailureAsync(authorizationResponse, accountUid))
            {
                return;
            }

            if (!await LoadProfilePictureAsync())
            {
                return;
            }

            await _syncService.SyncPendingOperationsAsync();

            var dashboardResponse = await _apiService.GetGoerDashboardForRefreshAsync();
            if (await HandleOperationalFailureAsync(dashboardResponse, accountUid))
            {
                return;
            }

            var currentResponse = await _apiService.GetGoerCurrentAttendanceForRefreshAsync();
            if (await HandleOperationalFailureAsync(currentResponse, accountUid))
            {
                return;
            }

            var historyResponse = await _apiService.GetGoerAttendanceHistoryForRefreshAsync(
                fromGymDate: null,
                endExclusiveGymDate: null,
                page: 1,
                pageSize: 10);
            if (await HandleOperationalFailureAsync(historyResponse, accountUid))
            {
                return;
            }

            DashboardData = dashboardResponse.Data!;
            DashboardData.CurrentSession = currentResponse.Data!.Session;
            OnPropertyChanged(nameof(DashboardData));
            ReplaceRecentAttendance(historyResponse.Data!.Items);

            AttendanceStatusMessage = string.Empty;
            await SaveCurrentCacheAsync(accountUid);
        }
        catch (Exception exception) when (exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException)
        {
            AttendanceErrorMessage = exception.Message;
            await LoadCachedAsync(accountUid, "Server unavailable: showing cached dashboard data.");
        }
        catch (Exception)
        {
            await InvalidateRejectedSessionAsync();
        }
        finally
        {
            IsAttendanceLoading = false;
        }
    }

    private async Task<bool> LoadProfilePictureAsync()
    {
        var result = await _apiService.GetCurrentProfilePictureForRefreshAsync();
        if (result.Status == OperationalResourceStatus.Rejected)
        {
            await InvalidateRejectedSessionAsync();
            return false;
        }

        if (result.Status == OperationalResourceStatus.Success && result.Data is not null)
        {
            var imageBytes = result.Data.Bytes;
            ProfilePictureSource = ImageSource.FromStream(
                () => new MemoryStream(imageBytes, writable: false));
            HasProfilePicture = true;
            ShowDefaultProfilePicture = false;
            return true;
        }

        // Missing or temporarily unavailable private imagery uses an in-memory
        // default. No protected bytes are persisted for offline use.
        ProfilePictureSource = null;
        HasProfilePicture = false;
        ShowDefaultProfilePicture = true;
        return true;
    }

    private async Task<bool> HandleOperationalFailureAsync<T>(
        OperationalResourceResult<T> result,
        string accountUid)
    {
        if (result.Status == OperationalResourceStatus.Success && result.Data is not null)
        {
            return false;
        }

        if (result.Status == OperationalResourceStatus.Unavailable)
        {
            AttendanceErrorMessage = result.Message ?? string.Empty;
            await LoadCachedAsync(
                accountUid,
                "Server unavailable: showing cached dashboard data.");
            return true;
        }

        await InvalidateRejectedSessionAsync();
        return true;
    }

    private async Task InvalidateRejectedSessionAsync()
    {
        if (Interlocked.Exchange(ref _sessionInvalidationStarted, 1) != 0)
        {
            return;
        }

        AttendanceErrorMessage = "Your app access is no longer active. Please sign in again.";
        try
        {
            await _logoutService.LogoutAsync(CancellationToken.None);
        }
        catch
        {
            // Navigation still returns to the login shell; session cleanup is already
            // best-effort and cannot be retried concurrently from this view model.
        }

        if (!_rootNavigationService.TrySetRoot(_appShellFactory()))
        {
            AttendanceErrorMessage = "App access ended, but the login screen could not be displayed.";
        }
    }

    private bool IsSessionRejected =>
        Volatile.Read(ref _sessionInvalidationStarted) != 0;

    private async Task LoadCachedAsync(string accountUid, string statusMessage)
    {
        var cached = await _databaseService.GetGoerDashboardAsync(accountUid);
        if (cached is null)
        {
            AttendanceErrorMessage = string.IsNullOrWhiteSpace(AttendanceErrorMessage)
                ? "No cached dashboard is available for this account."
                : AttendanceErrorMessage;
            return;
        }

        DashboardData = cached.Dashboard;
        ReplaceRecentAttendance(cached.RecentAttendance);
        AttendanceStatusMessage = statusMessage;
    }

    private void ReplaceRecentAttendance(IEnumerable<AttendanceDto> items)
    {
        RecentAttendance.Clear();
        foreach (var item in items)
        {
            RecentAttendance.Add(item);
        }

        IsAttendanceEmpty = RecentAttendance.Count == 0;
    }

    private Task SaveCurrentCacheAsync(string accountUid) =>
        _databaseService.SaveGoerDashboardAsync(
            accountUid,
            DashboardData,
            RecentAttendance.ToList());

    private string RequireAccountUid()
    {
        var accountUid = _authService.GetCurrentUid();
        return !string.IsNullOrWhiteSpace(accountUid)
            ? accountUid
            : throw new InvalidOperationException("A Firebase session is required.");
    }
}
