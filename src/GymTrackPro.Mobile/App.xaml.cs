using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using GymTrackPro.Mobile.Services;

namespace GymTrackPro.Mobile;

public partial class App : Application
{
	private readonly ILocalDatabaseService _databaseService;
	private readonly IApiService _apiService;

	public App(ILocalDatabaseService databaseService, IApiService apiService)
	{
		InitializeComponent();
		_databaseService = databaseService;
		_apiService = apiService;

		// Initialize local SQLite database and JWT auth token asynchronously on startup
		Task.Run(async () =>
		{
			await _databaseService.InitializeAsync();
			await _apiService.InitializeTokenAsync();
		});
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}