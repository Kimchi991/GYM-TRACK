using GymTrackPro.Mobile.Views;

namespace GymTrackPro.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("register", typeof(RegisterPage));
        Routing.RegisterRoute("forgotpassword", typeof(ForgotPasswordPage));
        Routing.RegisterRoute("resetpassword", typeof(ResetPasswordPage));
        Routing.RegisterRoute("memberdetails", typeof(MemberDetailsPage));
        Routing.RegisterRoute("plans", typeof(PlansPage));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var authService = Handler?.MauiContext?.Services.GetService<GymTrackPro.Mobile.Services.IFirebaseAuthService>();
        var apiService = Handler?.MauiContext?.Services.GetService<GymTrackPro.Mobile.Services.IApiService>();

        if (authService != null && apiService != null)
        {
            var hasSession = await authService.HasValidSessionAsync();
            if (hasSession)
            {
                var meResult = await apiService.GetCurrentUserAsync();
                if (meResult.Success && meResult.Data != null)
                {
                    if (meResult.Data.Role == GymTrackPro.Shared.Enums.UserRole.GymGoer)
                    {
                        Application.Current.MainPage = new GoerAppShell();
                    }
                    else
                    {
                        await Shell.Current.GoToAsync("///dashboard");
                    }
                }
            }
        }
    }
}
