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
    /// <summary>Manual per-battle progress: medals, Lightning Victory and milestones.</summary>
    public const string ManualBattles = "ManualBattles";

    /// <summary>Copying a tester's production data into their staging account (see <c>Features/StagingTransfer</c>).
    /// Turn it on in both App Configuration stores; the web app reads it to show the copy actions.</summary>
    public const string StagingTransfer = "StagingTransfer";
}
