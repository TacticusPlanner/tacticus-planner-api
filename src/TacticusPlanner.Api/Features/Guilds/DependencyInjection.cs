namespace TacticusPlanner.Api.Features.Guilds;

public static class DependencyInjection
{
    public static IServiceCollection AddGuildsFeature(this IServiceCollection services)
    {
        services.AddScoped<GuildSyncService>();
        services.AddScoped<GuildRaidStatusService>();
        services.AddSingleton<GuildRaidRefreshCoordinator>();

        return services;
    }
}
