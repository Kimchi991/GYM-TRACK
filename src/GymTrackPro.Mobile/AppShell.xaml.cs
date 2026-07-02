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
}
