namespace GymTrackPro.Mobile;

public partial class GoerAppShell : Shell
{
    public GoerAppShell(
        Views.GoerDashboardPage dashboardPage,
        Views.GoerProgressPage progressPage,
        Views.GoerDigitalCardPage digitalCardPage)
    {
        InitializeComponent();
        DashboardContent.Content = dashboardPage
            ?? throw new ArgumentNullException(nameof(dashboardPage));
        ProgressContent.Content = progressPage
            ?? throw new ArgumentNullException(nameof(progressPage));
        DigitalCardContent.Content = digitalCardPage
            ?? throw new ArgumentNullException(nameof(digitalCardPage));
    }
}
