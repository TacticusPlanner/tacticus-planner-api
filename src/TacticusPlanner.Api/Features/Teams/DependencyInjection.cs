namespace TacticusPlanner.Api.Features.Teams;

public static class DependencyInjection
{
    public static IServiceCollection AddTeamsFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TeamOptions>()
            .Bind(configuration.GetSection(TeamOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<TeamsService>();

        return services;
    }
}
