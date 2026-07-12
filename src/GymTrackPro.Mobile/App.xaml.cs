namespace GymTrackPro.Mobile;

public partial class App : Application
{
	private readonly AppShell _appShell;

	public App(AppShell appShell)
	{
		InitializeComponent();
		_appShell = appShell ?? throw new ArgumentNullException(nameof(appShell));
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_appShell);
	}
}
