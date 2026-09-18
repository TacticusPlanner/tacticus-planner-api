namespace TacticusPlanner.Api.Features.PlayerData;

public static class DependencyInjection
{
    public static IServiceCollection AddPlayerDataFeature(this IServiceCollection services)
    {
        services.AddScoped<PlayerDataTransformer>();
        services.AddScoped<PlayerDataSyncService>();

        return services;
    }
}
