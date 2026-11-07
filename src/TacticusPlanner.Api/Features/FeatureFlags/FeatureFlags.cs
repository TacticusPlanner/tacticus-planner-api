namespace TacticusPlanner.Api.Features.FeatureFlags;

/// <summary>
/// Names of the feature flags the API itself checks. Unfinished work merged to <c>main</c> stays dark behind one
/// of these until it is ready: staging and production turn it on in their Azure App Configuration store. A flag
/// the web app alone checks needs no entry here, only configuration.
/// </summary>
/// <remarks>
/// Delete a flag, its checks, and its configuration once the feature has shipped everywhere.
/// </remarks>
public static class FeatureFlags
{
}
