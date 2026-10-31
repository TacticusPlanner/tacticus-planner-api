using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.FeatureManagement;

namespace TacticusPlanner.Api.Features.FeatureFlags;

public static class DependencyInjection
{
    public static WebApplicationBuilder AddFeatureFlagsFeature(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration
            .GetSection(AppConfigurationOptions.SectionName)
            .Get<AppConfigurationOptions>() ?? new AppConfigurationOptions();

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            var credential = new ManagedIdentityCredential(
                string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
                    ? ManagedIdentityId.SystemAssigned
                    : ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId)
            );

            // Optional: a store that is unreachable or throttled at startup must not keep the API down. Every flag
            // then reads as off, which is the safe state for unfinished work, until the next successful refresh.
            builder.Configuration.AddAzureAppConfiguration(
                appConfiguration => appConfiguration
                    .Connect(new Uri(options.Endpoint), credential)
                    // Flags only: a key filter matching nothing keeps any plain key-values in the store out of configuration.
                    .Select("__no_key_values__")
                    .UseFeatureFlags(flags => flags.SetRefreshInterval(options.RefreshInterval))
                    .ConfigureStartupOptions(startup => startup.Timeout = TimeSpan.FromSeconds(15)),
                optional: true
            );
            builder.Services.AddAzureAppConfiguration();
        }

        builder.Services.AddFeatureManagement();

        return builder;
    }

    /// <summary>
    /// Refreshes App Configuration flags (at most once per <see cref="AppConfigurationOptions.RefreshInterval"/>) as
    /// requests arrive. A no-op when no store is configured.
    /// </summary>
    public static WebApplication UseFeatureFlags(this WebApplication app)
    {
        if (app.Services.GetService<IConfigurationRefresherProvider>() is { } refresherProvider)
        {
            // Fire and forget, like the provider's own ASP.NET Core middleware: TryRefreshAsync returns at once
            // until the interval elapses, and a failed refresh keeps the last values and logs.
            app.Use((context, next) =>
            {
                foreach (var refresher in refresherProvider.Refreshers)
                {
                    _ = refresher.TryRefreshAsync(CancellationToken.None);
                }

                return next(context);
            });
        }

        return app;
    }
}
