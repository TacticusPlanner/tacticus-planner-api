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
                BuildTrackView(lre.Alpha, roster),
                BuildTrackView(lre.Beta, roster),
                BuildTrackView(lre.Gamma, roster),
                lre.PointsMilestones,
                lre.ChestsMilestones,
                lre.ShardsPerChest,
                lre.Progression))
            .ToArray();
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
            track.Battles,
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
