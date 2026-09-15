namespace TacticusPlanner.Api.Features.UserJot;

public static class DependencyInjection
{
    public static IServiceCollection AddUserJotFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<UserJotOptions>(configuration.GetSection(UserJotOptions.SectionName));
        services.AddScoped<UserJotTokenSigner>();

        return services;
    }
}
