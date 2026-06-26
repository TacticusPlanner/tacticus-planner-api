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
                lre.WikiLink,
                lre.EventStage,
                lre.Finished,
                lre.NextEventDate,
                lre.NextEventDateUtc,
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
