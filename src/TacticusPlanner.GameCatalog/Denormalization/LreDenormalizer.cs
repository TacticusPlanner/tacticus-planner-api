using System.Globalization;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    public static IReadOnlyList<GameCatalogLreView> BuildLres(
        IReadOnlyDictionary<string, GameCatalogLre> lresByEvent,
        IReadOnlyDictionary<string, GameCatalogFactionUnits> unitsByFaction)
    {
        var roster = unitsByFaction
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .SelectMany(faction => faction.Characters.Select(character =>
                (character.Id, Faction: faction.FactionId, faction.Alliance)))
            .ToArray();

        return lresByEvent
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .Select(lre => new GameCatalogLreView(
                lre.UnitSnowprintId,
                lre.Name,
                lre.Finished,
                BuildEventStageStartDates(lre.NextEventDateUtc),
                lre.BattlesCount,
                lre.ConstraintsCount,
                lre.RegularMissions,
                lre.PremiumMissions,
                BuildTrackView(lre.UnitSnowprintId, "alpha", lre.Alpha, roster),
                BuildTrackView(lre.UnitSnowprintId, "beta", lre.Beta, roster),
                BuildTrackView(lre.UnitSnowprintId, "gamma", lre.Gamma, roster)))
            .ToArray();
    }

    private static readonly (string Key, Func<GameCatalogLre, GameCatalogLreTrack> Track)[] LreTrackSelectors =
    [
        ("alpha", lre => lre.Alpha),
        ("beta", lre => lre.Beta),
        ("gamma", lre => lre.Gamma),
    ];

    private static string LreBattleId(string lreId, string track, int number) => $"{lreId}-{track}-{number}";

    // lre-battles: every battle flattened across all events × tracks, each carrying its lreId + track and a
    // composite id that the owning track's BattleIds resolve to.
    public static IReadOnlyList<GameCatalogLreBattleView> BuildLreBattles(
        IReadOnlyDictionary<string, GameCatalogLre> lresByEvent) =>
        lresByEvent
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .SelectMany(lre => LreTrackSelectors.SelectMany(selector =>
                selector.Track(lre).Battles.Select(battle => new GameCatalogLreBattleView(
                    LreBattleId(lre.UnitSnowprintId, selector.Key, battle.Number),
                    lre.UnitSnowprintId,
                    selector.Key,
                    battle.MapId,
                    battle.Number,
                    battle.Power,
                    battle.Tier,
                    battle.DisallowedFactions,
                    battle.Waves))))
            .ToArray();

    // lre-common: the reward ladder is identical across every event, so serve it once as a single record.
    public static IReadOnlyList<GameCatalogLreCommon> BuildLreCommon(
        IReadOnlyDictionary<string, GameCatalogLre> lresByEvent)
    {
        var source = lresByEvent
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .First();

        return
        [
            new GameCatalogLreCommon(
                GameCatalogDatasets.LreCommon,
                source.PointsMilestones,
                source.ChestsMilestones,
                source.Progression,
                source.ShardsPerChest),
        ];
    }

    // The source carries one upcoming-event date (e.g. "Sun, 01 February 2026 00:00:00 GMT"). Normalize it
    // to ISO 8601 UTC and expose it as the per-event-stage start-date array (one element today); the client
    // derives the current event stage from this array.
    private static IReadOnlyList<string> BuildEventStageStartDates(string? nextEventDateUtc)
    {
        if (string.IsNullOrWhiteSpace(nextEventDateUtc)
            || !DateTimeOffset.TryParse(nextEventDateUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return [];
        }

        return [parsed.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)];
    }

    private static GameCatalogLreTrackView BuildTrackView(
        string lreId,
        string trackKey,
        GameCatalogLreTrack track,
        IReadOnlyList<(string Id, string Faction, string Alliance)> roster)
    {
        var availableUnitIds = roster
            .Where(unit => track.AllowedUnitsFilter.All(filter => Passes(filter, unit.Faction, unit.Alliance)))
            .Select(unit => unit.Id)
            .ToArray();

        return new GameCatalogLreTrackView(
            track.Name,
            track.Enemies,
            track.KillPoints,
            track.BattlesPoints,
            track.DefeatAll,
            track.AllowedUnitsFilter,
            track.UnitsRestrictions,
            track.Battles.Select(battle => LreBattleId(lreId, trackKey, battle.Number)).ToArray(),
            availableUnitIds);
    }

    private static bool Passes(GameCatalogLreFilter filter, string faction, string alliance)
    {
        var matches = filter.Kind switch
        {
            "Alliance" => string.Equals(alliance, filter.Target, StringComparison.Ordinal),
            "Faction" => string.Equals(faction, filter.Target, StringComparison.Ordinal),
            _ => false,
        };

        return filter.Exclude ? !matches : matches;
    }
}
