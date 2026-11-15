namespace TacticusPlanner.Api.Features.EventShopPlans;

public static class DependencyInjection
{
    public static IServiceCollection AddEventShopPlansFeature(this IServiceCollection services)
    {
        services.AddScoped<EventShopPlanCatalogValidator>();
        services.AddScoped<EventShopPlanWriter>();

        return services;
    }
}
