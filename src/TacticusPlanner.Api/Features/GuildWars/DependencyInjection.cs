namespace TacticusPlanner.Api.Features.GuildWars;

public static class DependencyInjection
{
    public static IServiceCollection AddGuildWarsFeature(this IServiceCollection services)
    {
        services.AddScoped<GuildWarsService>();
        return services;
    }
}
