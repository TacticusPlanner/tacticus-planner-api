namespace TacticusPlanner.Api.Features.UserJot;

public static class DependencyInjection
{
    public static IServiceCollection AddUserJotFeature(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateOnStart = true
    )
    {
        var optionsBuilder = services
            .AddOptions<UserJotOptions>()
            .Bind(configuration.GetSection(UserJotOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProjectId), "UserJot:ProjectId is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProjectSecret), "UserJot:ProjectSecret is required.");

        // Skipped during OpenAPI doc generation (see Program.cs's isOpenApiDocumentGeneration), which starts
        // the host against the committed appsettings.json - deliberately missing the real secret - to inspect
        // endpoint metadata. Real runtime always validates.
        if (validateOnStart)
        {
            optionsBuilder.ValidateOnStart();
        }

        services.AddScoped<UserJotTokenSigner>();

        return services;
    }
}
