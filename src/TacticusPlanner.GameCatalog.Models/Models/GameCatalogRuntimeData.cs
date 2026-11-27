namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The raw catalog data the API reads at runtime: the events calendar inputs (the calendar is projected per UTC day)
/// and the few raw facts no served view carries. Keep this list short: a runtime consumer reads a served view when
/// one has the fields it needs.
/// </summary>
/// <param name="EventDefinitions">The authored event definitions (calendar input).</param>
/// <param name="EventOccurrences">The authored event occurrences (calendar input; also guild raid season ends).</param>
/// <param name="LreUnitIdsByV1Id">The unit snowprint id of each raw Legendary Event by its numeric V1 id
/// (<c>LegendaryEventEnum</c>), for the V1 import. Served or not: the caller checks the served <c>lres</c>.</param>
/// <param name="RaidBossSeasonLoops">Where each guild raid season config loops back to after its last boss, by
/// season config id. A config that does not loop is absent.</param>
public sealed record GameCatalogRuntimeData(
    IReadOnlyList<GameCatalogEventDefinition> EventDefinitions,
    IReadOnlyList<GameCatalogEventOccurrence> EventOccurrences,
    IReadOnlyDictionary<int, string> LreUnitIdsByV1Id,
    IReadOnlyDictionary<string, GameCatalogRaidBossSeasonLoop> RaidBossSeasonLoops);

/// <summary>The tier and set a guild raid season config loops back to after its last boss.</summary>
public sealed record GameCatalogRaidBossSeasonLoop(int Tier, int Set);
