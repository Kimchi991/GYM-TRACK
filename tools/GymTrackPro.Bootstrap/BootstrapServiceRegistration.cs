using GymTrackPro.API.Authentication;
using GymTrackPro.API.Data;
using GymTrackPro.API.Services;
using GymTrackPro.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GymTrackPro.Bootstrap;

public static class BootstrapServiceRegistration
{
    public static IServiceCollection AddBootstrapCore(
        this IServiceCollection services,
        BootstrapCommand command,
        Action<DbContextOptionsBuilder> configureDatabase)
    {
        services.AddSingleton<IHostEnvironment>(new BootstrapHostEnvironment(
            command.EnvironmentName));
        services.Configure<OwnerBootstrapOptions>(options =>
        {
            options.Enabled = true;
            options.AllowedEnvironment = command.EnvironmentName;
        });
        services.AddDbContext<GymDbContext>(configureDatabase);
        services.AddScoped<IClockService, ClockService>();
        services.AddScoped<IOwnerBootstrapService, OwnerBootstrapService>();
        return services;
    }

    private sealed class BootstrapHostEnvironment : IHostEnvironment
    {
        public BootstrapHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "GymTrackPro.Bootstrap";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
