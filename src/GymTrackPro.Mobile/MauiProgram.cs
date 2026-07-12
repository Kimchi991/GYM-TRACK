using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Mobile.ViewModels;
using GymTrackPro.Mobile.Views;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace GymTrackPro.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Register ViewModels
		builder.Services.AddTransient<LoginViewModel>();
		builder.Services.AddTransient<RegisterViewModel>();
		builder.Services.AddTransient<ForgotPasswordViewModel>();
		builder.Services.AddTransient<ResetPasswordViewModel>();
		builder.Services.AddTransient<DashboardViewModel>();
		builder.Services.AddTransient<MembersViewModel>();
		builder.Services.AddTransient<AttendanceViewModel>();
		builder.Services.AddTransient<PaymentsViewModel>();
		builder.Services.AddTransient<ReportsViewModel>();
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<NotificationsViewModel>();
		builder.Services.AddTransient<PlansViewModel>();
		builder.Services.AddTransient<MemberDetailsViewModel>();
		builder.Services.AddTransient<StaffProvisioningViewModel>();

		// Register Views (Pages)
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<RegisterPage>();
		builder.Services.AddTransient<ForgotPasswordPage>();
		builder.Services.AddTransient<ResetPasswordPage>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<MembersPage>();
		builder.Services.AddTransient<AttendancePage>();
		builder.Services.AddTransient<PaymentsPage>();
		builder.Services.AddTransient<ReportsPage>();
		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddTransient<NotificationsPage>();
		builder.Services.AddTransient<PlansPage>();
		builder.Services.AddTransient<MemberDetailsPage>();
		builder.Services.AddTransient<StaffProvisioningPage>();

		// Register Gym Goer ViewModels and Views
		builder.Services.AddTransient<GoerDashboardViewModel>();
		builder.Services.AddTransient<GoerDigitalCardViewModel>();
		builder.Services.AddTransient<GoerProgressViewModel>();
		builder.Services.AddTransient<GoerDashboardPage>();
		builder.Services.AddTransient<GoerDigitalCardPage>();
		builder.Services.AddTransient<GoerProgressPage>();
		builder.Services.AddTransient<GoerAppShell>();
		builder.Services.AddTransient<AppShell>();
		builder.Services.AddTransient<Func<GoerAppShell>>(services =>
			() => services.GetRequiredService<GoerAppShell>());
		builder.Services.AddTransient<Func<AppShell>>(services =>
			() => services.GetRequiredService<AppShell>());

		// Register SQLite local database connection & SyncQueue services
		builder.Services.AddSingleton<ILocalDatabaseService, LocalDatabaseService>();
		builder.Services.AddSingleton<INetworkService, NetworkService>();
		builder.Services.AddSingleton<ISyncService, SyncService>();
		builder.Services.AddSingleton<IApiService, ApiService>();
		builder.Services.AddSingleton<IFirebaseAuthService, FirebaseAuthService>();
		builder.Services.AddSingleton<IAccountLocalDataCleaner>(services =>
			services.GetRequiredService<ILocalDatabaseService>() as IAccountLocalDataCleaner
			?? throw new InvalidOperationException("The local database must implement account cleanup."));
		builder.Services.AddSingleton<IAccountSessionInvalidator, AccountSessionInvalidator>();
		builder.Services.AddSingleton<IAppLogoutService, AppLogoutService>();
		builder.Services.AddSingleton<IRootNavigationService, MauiRootNavigationService>();
		builder.Services.AddSingleton<IAppDialogService, MauiAppDialogService>();
		builder.Services.AddSingleton<IAppClipboardService, MauiAppClipboardService>();

		// Register Firebase notification receiver services (Phase 10)

		// Explicitly initialize SQLite for Android/iOS
		SQLitePCL.Batteries_V2.Init();

		return builder.Build();
	}
}
