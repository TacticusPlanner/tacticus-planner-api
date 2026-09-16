namespace TacticusPlanner.Api.Features.Analytics;

public static class DependencyInjection
{
    public static IServiceCollection AddAnalyticsFeature(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateOnStart = true
    )
    {
        var optionsBuilder = services
            .AddOptions<AnalyticsOptions>()
            .Bind(configuration.GetSection(AnalyticsOptions.SectionName))
            .Validate(
                options => options.TryDecodeIdentityKey(out _),
                "Analytics:IdentityKey is required and must be a 32-byte base64 key."
            );

        // Skipped during OpenAPI doc generation (see Program.cs's isOpenApiDocumentGeneration), which starts
        // the host against the committed appsettings.json - deliberately missing the real key - to inspect
        // endpoint metadata. Real runtime always validates.
        if (validateOnStart)
        {
            optionsBuilder.ValidateOnStart();
        }

        services.AddSingleton<AnalyticsIdentityDeriver>();

        // Analytics:ProjectToken is deliberately not validated above - its absence is the supported way to
        // run with capture switched off (design.md's "identity key is required; the project token is
        // optional" decision). Read directly from configuration rather than IOptions<AnalyticsOptions>: this
        // choice is made once at startup, not per-request.
        var projectToken = configuration[$"{AnalyticsOptions.SectionName}:ProjectToken"];
        if (!string.IsNullOrWhiteSpace(projectToken))
        {
            services.AddPostHogProductAnalytics(configuration);
        }
        else
        {
            services.AddSingleton<IProductAnalytics, NullProductAnalytics>();
        }

        return services;
    }
}
