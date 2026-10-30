namespace TacticusPlanner.Api.Features.FeatureFlags;

/// <summary>
/// Names of the feature flags the API itself checks. Unfinished work merged to <c>main</c> stays dark behind one
/// of these until it is ready: staging turns a flag on in <c>appsettings.Staging.json</c>, production in Azure App
/// Configuration. A flag the web app alone checks needs no entry here, only configuration.
/// </summary>
/// <remarks>
/// Delete a flag, its checks, and its configuration once the feature has shipped everywhere.
/// </remarks>
public static class FeatureFlags
{
}
