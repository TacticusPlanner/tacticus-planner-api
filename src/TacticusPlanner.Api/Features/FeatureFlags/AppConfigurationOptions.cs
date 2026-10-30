namespace TacticusPlanner.Api.Features.FeatureFlags;

/// <summary>
/// Azure App Configuration store that production reads its feature flags from. Absent (the default, and every
/// environment but production) leaves flags to <c>appsettings*.json</c> and environment variables.
/// </summary>
public sealed class AppConfigurationOptions
{
    public const string SectionName = "AppConfiguration";

    /// <summary>Store endpoint, such as <c>https://appcs-tp-prod.azconfig.io</c>. Empty disables the store.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Client id of the user-assigned identity allowed to read the store.</summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    /// <summary>
    /// How long flag values are cached before a request triggers a re-read. The free tier allows 1,000 requests a
    /// day per store, and each replica spends one per refresh: 5 minutes keeps three replicas under that.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
}
