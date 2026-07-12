using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public static class ProjectionVersionServiceCollectionExtensions
{
    public static IServiceCollection AddProjectionVersionInfrastructure(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IProjectionVersionProvider, ProjectionVersionProvider>();
        return services;
    }
}
