namespace TacticusPlanner.Api.Features.Goals;

public static class DependencyInjection
{
    public static IServiceCollection AddGoalsFeature(this IServiceCollection services)
    {
        services.AddScoped<GoalTargetValidationService>();

        return services;
    }
}
