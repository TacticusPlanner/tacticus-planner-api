namespace TacticusPlanner.Api.Features.RosterSnapshots;

public static class DependencyInjection
{
    public static IServiceCollection AddRosterSnapshotsFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RosterSnapshotOptions>()
            .Bind(configuration.GetSection(RosterSnapshotOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<PersonalRosterSnapshotService>();

        return services;
    }
}
