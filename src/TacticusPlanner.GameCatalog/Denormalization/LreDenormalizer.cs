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
                BuildEventStageStartDates(lre.RunStartDatesUtc),
                lre.BattlesCount,
                lre.ConstraintsCount,
                lre.RegularMissions,
                lre.PremiumMissions,
                BuildTrackView(lre.UnitSnowprintId, "alpha", lre.Alpha, roster),
                BuildTrackView(lre.UnitSnowprintId, "beta", lre.Beta, roster),
                BuildTrackView(lre.UnitSnowprintId, "gamma", lre.Gamma, roster),
                new GameCatalogLreRewards(lre.PointsMilestones, lre.ChestsMilestones, lre.Progression, lre.ShardsPerChest)))
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

    // The raw file lists the announced run starts in run order (e.g. "Sun, 08 March 2026 00:00:00 GMT").
    // Normalise each to ISO 8601 UTC, keeping the order, so the served index is the run number minus one.
    // An unparseable entry is passed through unchanged; validation (ValidateLreRunStarts, which runs after
    // denormalization) rejects it, so it never reaches a served payload.
    private static string[] BuildEventStageStartDates(IReadOnlyList<string> runStartDatesUtc) =>
        runStartDatesUtc
            .Select(start => ParseLreRunStart(start)?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                ?? start)
            .ToArray();

    // Accepted raw run start formats: RFC 1123 with an abbreviated or full month name, and ISO 8601 in UTC.
    // Exact formats keep partial values such as "8 March" or "12:00" from filling in from the current clock.
    private static readonly string[] LreRunStartFormats =
    [
        "r",
        "ddd, dd MMMM yyyy HH:mm:ss 'GMT'",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    ];

    // Parses one raw run start as a UTC instant; null when it is not a full date in an accepted format.
    internal static DateTimeOffset? ParseLreRunStart(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && DateTimeOffset.TryParseExact(value, LreRunStartFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

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
