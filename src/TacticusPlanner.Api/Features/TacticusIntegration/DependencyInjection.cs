namespace TacticusPlanner.Api.Features.TacticusIntegration;

public static class DependencyInjection
{
    public static IServiceCollection AddTacticusIntegrationFeature(this IServiceCollection services)
    {
        services.AddScoped<TacticusApiKeyValidator>();

        return services;
    }
}
