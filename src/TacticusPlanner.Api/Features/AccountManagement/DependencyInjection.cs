namespace TacticusPlanner.Api.Features.AccountManagement;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountManagementFeature(this IServiceCollection services)
    {
        services.AddSingleton<IExternalIdentityDeleter, NoOpExternalIdentityDeleter>();

        return services;
    }
}
