namespace TacticusPlanner.Api.Features.V1Import;

public static class DependencyInjection
{
    public static IServiceCollection AddV1ImportFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<V1GoalImportService>();
        services.AddTacticusV1Client(
            configuration["V1Api:BaseUrl"],
            configuration["V1Api:FunctionsKey"]
        );

        return services;
    }
}
