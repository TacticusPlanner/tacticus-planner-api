using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    private const int DefaultPowerMultiplier = 100;

    /// <summary>
    /// Builds the single served <c>unit-power</c> record from the raw file: progression steps get their
    /// index, the modifier maps become lists sorted by id (ordinal), and <c>lineup</c> becomes one unit per
    /// catalog character or MoW, sorted by unit id. Lineup entries the catalog does not have (unreleased
    /// units) are skipped; a missing <c>powerMultiplier</c> is served as 100; a character's stat increases
    /// are truncated to its catalog <c>rankUpUpgrades</c> row count, and a MoW's are served empty. A
    /// catalog unit with no lineup entry is left out, so validation (over the raw file) reports it.
    /// </summary>
    public static IReadOnlyList<GameCatalogUnitPower> BuildUnitPower(
        GameCatalogRawUnitPower raw,
        IReadOnlyList<GameCatalogCharacter> characters,
        IReadOnlyList<GameCatalogMow> mows)
    {
        var lineup = raw.Lineup ?? new Dictionary<string, GameCatalogRawUnitPowerEntry>();
        var rankRowsByCharacter = characters.ToDictionary(
            character => character.Id, character => character.RankUpUpgrades.Count, StringComparer.Ordinal);
        var mowIds = new HashSet<string>(mows.Select(mow => mow.Id), StringComparer.Ordinal);

        var units = lineup
            .Where(pair => rankRowsByCharacter.ContainsKey(pair.Key) || mowIds.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new GameCatalogUnitPowerUnit(
                pair.Key,
                pair.Value.PowerMultiplier ?? DefaultPowerMultiplier,
                rankRowsByCharacter.TryGetValue(pair.Key, out var rankRows)
                    ? (pair.Value.UpgradesStatIncrease ?? []).Take(rankRows).ToArray()
                    : []))
            .ToArray();

        var record = new GameCatalogUnitPower(
            GameCatalogDatasets.UnitPower,
            (raw.HeroProgressionSteps ?? [])
                .Select((step, index) => new GameCatalogHeroPowerStep(index, step.UnitStatMultiplierPct, step.AbilityPowerMultiplier))
                .ToArray(),
            (raw.HeroProgressionStepsMoW ?? [])
                .Select((step, index) => new GameCatalogMowPowerStep(index, step.AbilityPowerMultiplier, step.MythicAbilityPower ?? 0))
                .ToArray(),
            (raw.DamageProfileModifiers ?? new Dictionary<string, int>())
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new GameCatalogDamageProfileModifier(pair.Key, pair.Value))
                .ToArray(),
            (raw.TraitPowerModifiers ?? new Dictionary<string, int>())
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new GameCatalogTraitPowerModifier(pair.Key, pair.Value))
                .ToArray(),
            (raw.AbilityPowerModifiers ?? new Dictionary<string, GameCatalogRawAbilityPowerModifier>())
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new GameCatalogAbilityPowerModifier(pair.Key, pair.Value.BaseMultiplier))
                .ToArray(),
            new GameCatalogAbilityPowerCurves(
                raw.AbilityPowerCurve?.Active ?? [],
                raw.AbilityPowerCurve?.Passive ?? [],
                raw.AbilityPowerCurve?.Relic ?? []),
            units);

        return [record];
    }
}
