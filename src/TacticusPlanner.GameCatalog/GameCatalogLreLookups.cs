using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>Lookups over the served Legendary Event views used by plan validation and the V1 import:
/// an event by its served id or by the raw numeric (V1 <c>LegendaryEventEnum</c>) id, and a lane by its
/// key. Lanes are served as the named <c>Alpha</c>/<c>Beta</c>/<c>Gamma</c> properties, so the key →
/// lane mapping lives here rather than being repeated by every consumer.</summary>
public static class GameCatalogLreLookups
{
    public const string AlphaLane = "alpha";
    public const string BetaLane = "beta";
    public const string GammaLane = "gamma";

    public static readonly IReadOnlyList<string> LaneKeys = [AlphaLane, BetaLane, GammaLane];

    public static GameCatalogLreView? FindLre(this GameCatalogSnapshot catalog, string? eventId) =>
        eventId is null
            ? null
            : catalog.LreViews.FirstOrDefault(lre => string.Equals(lre.Id, eventId, StringComparison.Ordinal));

    /// <summary>The served view of the event whose raw source carries <paramref name="rawId"/> — the
    /// numeric id V1 keys its <c>leTeams</c>/<c>leProgress</c> blobs by. Null when the catalog does not
    /// carry the event (finished events are not in the catalog yet).</summary>
    public static GameCatalogLreView? FindLreByRawId(this GameCatalogSnapshot catalog, int rawId)
    {
        var raw = catalog.Lres.FirstOrDefault(lre => lre.Id == rawId);
        return raw is null ? null : catalog.FindLre(raw.UnitSnowprintId);
    }

    public static GameCatalogLreTrackView? Lane(this GameCatalogLreView lre, string? laneKey) => laneKey switch
    {
        AlphaLane => lre.Alpha,
        BetaLane => lre.Beta,
        GammaLane => lre.Gamma,
        _ => null,
    };

    public static IEnumerable<(string Key, GameCatalogLreTrackView Lane)> Lanes(this GameCatalogLreView lre) =>
    [
        (AlphaLane, lre.Alpha),
        (BetaLane, lre.Beta),
        (GammaLane, lre.Gamma),
    ];
}
