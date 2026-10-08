using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog;

/// <summary>Legendary Event lookups shared by plan validation and the V1 import.</summary>
public static class GameCatalogLreLookups
{
    /// <summary>The served <c>lres</c> id for a V1 <c>LegendaryEventEnum</c> value: the raw event whose numeric id
    /// matches, provided it is also served. Null for an event the catalog does not carry.</summary>
    public static string? ServedLreIdForV1Id(this GameCatalogSnapshot catalog, int v1EventId)
    {
        var raw = catalog.Lres.FirstOrDefault(lre => lre.Id == v1EventId);
        return raw is not null && catalog.LreViews.Any(view => view.Id == raw.UnitSnowprintId)
            ? raw.UnitSnowprintId
            : null;
    }

    /// <summary>The lane for a catalog track key (<c>alpha</c>, <c>beta</c>, <c>gamma</c>); null otherwise.</summary>
    public static GameCatalogLreTrackView? Lane(this GameCatalogLreView lre, string? laneId) => laneId switch
    {
        "alpha" => lre.Alpha,
        "beta" => lre.Beta,
        "gamma" => lre.Gamma,
        _ => null,
    };
}
