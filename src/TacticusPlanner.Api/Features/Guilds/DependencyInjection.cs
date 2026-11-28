namespace TacticusPlanner.Api.Features.Guilds;

public static class DependencyInjection
{
    public static IServiceCollection AddGuildsFeature(this IServiceCollection services)
    {
        services.AddScoped<GuildSyncService>();
        services.AddScoped<GuildClaimEvaluator>();
        services.AddScoped<GuildPlayerRegistry>();
        services.AddScoped<GuildRaidStatusService>();
        services.AddSingleton<GuildRaidRefreshCoordinator>();
        services.AddScoped<GuildRaidEndedSeasonService>();
        services.AddScoped<GuildRosterRefreshService>();
        services.AddScoped<PlayerData.IPlayerDataSyncDefaultStep, GuildClaimSyncStep>();

        return services;
    }
}
