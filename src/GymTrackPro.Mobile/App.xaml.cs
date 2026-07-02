using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile;

public partial class App : Application
{
	private readonly ILocalDatabaseService _databaseService;

	public App(ILocalDatabaseService databaseService)
	{
		InitializeComponent();
		_databaseService = databaseService;

		// Initialize local SQLite database asynchronously on startup
		Task.Run(async () => await _databaseService.InitializeAsync());
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}