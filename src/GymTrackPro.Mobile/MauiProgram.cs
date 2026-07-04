using Microsoft.Extensions.Logging;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Mobile.ViewModels;
using GymTrackPro.Mobile.Views;

namespace GymTrackPro.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
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

		// Register SQLite local database connection & SyncQueue services
		builder.Services.AddSingleton<ILocalDatabaseService, LocalDatabaseService>();
		builder.Services.AddSingleton<INetworkService, NetworkService>();
		builder.Services.AddSingleton<ISyncService, SyncService>();
		builder.Services.AddSingleton<IApiService, ApiService>();
		builder.Services.AddSingleton<IFirebaseAuthService, FirebaseAuthService>();

		// TODO: Register Firebase notification receiver services (Phase 10)

		return builder.Build();
	}
}
