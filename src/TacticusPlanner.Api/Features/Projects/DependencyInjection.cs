namespace TacticusPlanner.Api.Features.Projects;

public static class DependencyInjection
{
    public static IServiceCollection AddProjectsFeature(this IServiceCollection services)
    {
        services.AddScoped<ProjectsService>();
        services.AddScoped<ProjectGoalPlanningService>();

        return services;
    }
}
