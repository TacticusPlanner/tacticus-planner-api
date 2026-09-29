namespace TacticusPlanner.Api.Features.Goals;

public static class DependencyInjection
{
    public static IServiceCollection AddGoalsFeature(this IServiceCollection services)
    {
        services.AddScoped<GoalTargetValidationService>();
        services.AddScoped<GoalOrderService>();
        services.AddScoped<GoalTargetEditor>();
        services.AddScoped<GoalDetailsEditor>();
        services.AddScoped<GoalMembershipEditor>();

        return services;
    }
}
