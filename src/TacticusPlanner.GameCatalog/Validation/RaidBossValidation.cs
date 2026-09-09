using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static readonly HashSet<string> ValidRaidBossEncounterTypes =
        new(StringComparer.Ordinal) { "Boss", "Crystal" };

    private static void ValidateRaidBosses(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        ValidateRaidBosses(snapshot.RaidBossRawData, snapshot.RaidBossesView, errors);
    }

    /// <summary>
    /// Takes the raw + served raid-boss collections directly (not the whole snapshot) so it is
    /// unit-testable without constructing a <see cref="GameCatalogSnapshot"/>, mirroring
    /// <see cref="ValidateShops"/> / <see cref="ValidateEvents"/>. Fails the build if an encounter's unit or
    /// field-npc id, a modifier id, or a season <c>unitSet</c> reference does not resolve; if a unit's
    /// progression ladder is empty; if an encounter type is unrecognized; or if the served bosses/primes
    /// lists are empty.
    /// </summary>
    internal static void ValidateRaidBosses(
        GameCatalogRaidBossRawData raw,
        GameCatalogRaidBossesView view,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.RaidBosses;

        var unitSetIds = new HashSet<string>(raw.UnitSets.Keys, StringComparer.Ordinal);
        var modifierIds = new HashSet<string>(raw.Modifiers.Keys, StringComparer.Ordinal);

        if (view.Bosses.Count == 0)
        {
            errors.Add(new GameCatalogValidationError(dataset, "EmptyDataset", "Served raid-boss dataset has no bosses."));
        }

        if (view.Primes.Count == 0)
        {
            errors.Add(new GameCatalogValidationError(dataset, "EmptyDataset", "Served raid-boss dataset has no primes."));
        }

        // Only bosses and primes are served as top-level entries (with a faction, progression ladder, and
        // ability/trait id lists). Field npcs and loot objects are referenced by encounters by id only, so
        // their raw shape is not constrained here.
        foreach (var (unitSetId, unitSet) in raw.UnitSets)
        {
            if (GameCatalogDenormalizer.ClassifyRaidBossUnit(unitSetId) is null)
            {
                continue;
            }

            if (unitSet.Stats.Count == 0)
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "EmptyProgression", $"Raid boss/prime '{unitSetId}' has an empty stat progression."));
            }

            Require(dataset, unitSetId, unitSet.FactionId, "factionId", errors);

            if (AbilityAndTraitIds(unitSet).Any(string.IsNullOrWhiteSpace))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "RequiredField", $"Raid boss/prime '{unitSetId}' has a blank ability or trait id."));
            }
        }

        foreach (var (seasonId, season) in raw.Seasons)
        {
            foreach (var encounter in season.Tiers.SelectMany(tier => tier.Sets).SelectMany(set => set.Encounters))
            {
                var owner = $"{seasonId}[encounter {encounter.EncounterIndex}]";

                if (!ValidRaidBossEncounterTypes.Contains(encounter.GuildBossEncounterType))
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "InvalidEncounterType",
                        $"'{owner}' has unrecognized encounter type '{encounter.GuildBossEncounterType}'."));
                }

                RequireReference(
                    dataset, owner, "unitId",
                    GameCatalogDenormalizer.StripProgressionSuffix(encounter.UnitId), unitSetIds, errors);

                foreach (var npcId in new[] { encounter.Npc1Id, encounter.Npc2Id }.Concat(encounter.Enemies ?? []))
                {
                    if (string.IsNullOrWhiteSpace(npcId))
                    {
                        continue;
                    }

                    RequireReference(
                        dataset, owner, "fieldNpcIds",
                        GameCatalogDenormalizer.StripProgressionSuffix(npcId), unitSetIds, errors);
                }

                foreach (var modifier in encounter.Modifiers ?? [])
                {
                    RequireReference(dataset, owner, "modifier", modifier.Modifier, modifierIds, errors);
                }
            }
        }
    }

    private static IEnumerable<string> AbilityAndTraitIds(GameCatalogRaidBossRawUnitSet unitSet) =>
        (unitSet.ActiveAbilities ?? [])
            .Concat(unitSet.PassiveAbilities ?? [])
            .Concat(unitSet.RelicAbilities ?? [])
            .Concat(unitSet.Traits ?? []);
}
