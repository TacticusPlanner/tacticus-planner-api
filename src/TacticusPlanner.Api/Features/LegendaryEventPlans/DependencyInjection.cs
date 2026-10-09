namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

public static class DependencyInjection
{
    public static IServiceCollection AddLegendaryEventPlansFeature(this IServiceCollection services)
    {
        services.AddScoped<LegendaryEventCatalogValidator>();
        services.AddScoped<LegendaryEventPlanProjection>();
        services.AddScoped<LegendaryEventPlanWriter>();

        return services;
    }
}
