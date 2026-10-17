using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    /// <summary>Hero and MoW progression steps: Common 0 stars through Mythic wings (progression index 0..19).</summary>
    internal const int UnitPowerProgressionSteps = 20;

    /// <summary>The upgrade stat types the power formula adds to a rank's base stats.</summary>
    private static readonly HashSet<string> UnitPowerUpgradeStats =
        new(["Health", "Damage", "Armour"], StringComparer.Ordinal);

    private static void ValidateUnitPower(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors) =>
        ValidateUnitPower(
            snapshot.UnitPower,
            snapshot.Characters,
            snapshot.Mows,
            snapshot.Npcs,
            snapshot.Upgrades,
            snapshot.CharacterAbilityCosts,
            snapshot.Equipment,
            errors);

    /// <summary>
    /// Validates the raw <c>unit-power</c> file against the catalog it powers: both progression lists have
    /// exactly 20 steps with positive multipliers; every catalog character and MoW has a <c>lineup</c>
    /// entry; a character's <c>upgradesStatIncrease</c> has a row for each catalog <c>rankUpUpgrades</c>
    /// row, as long as that rank's <c>upgradeIds</c>; no value is negative; every rank-up upgrade id is a
    /// catalog upgrade raising Health, Damage or Armour; every non-empty character or NPC weapon damage
    /// type has a damage profile modifier; the active and passive curves reach the highest ability level
    /// <c>character-ability-costs</c> reaches; and no relic has more levels than the relic curve. Lineup
    /// units the catalog does not have are skipped, and a trait or ability with no modifier means 100.
    /// Each failure is an <c>InvalidUnitPower</c> error naming the offending id or value.
    /// </summary>
    internal static void ValidateUnitPower(
        GameCatalogRawUnitPower raw,
        IReadOnlyList<GameCatalogCharacter> characters,
        IReadOnlyList<GameCatalogMow> mows,
        IReadOnlyList<GameCatalogRawNpc> npcs,
        IReadOnlyList<GameCatalogUpgrade> upgrades,
        IReadOnlyList<GameCatalogCharacterAbilityCost> abilityCosts,
        IReadOnlyList<GameCatalogEquipment> equipment,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.UnitPower;
        const string code = "InvalidUnitPower";

        void Fail(string message) =>
            errors.Add(new GameCatalogValidationError(dataset, code, $"Dataset '{dataset}' {message}"));

        void RequireNotNegative(string what, int value)
        {
            if (value < 0)
            {
                Fail($"{what} '{value}' must not be negative.");
            }
        }

        // ---- progression steps ----------------------------------------------------------------
        var heroSteps = raw.HeroProgressionSteps ?? [];
        if (heroSteps.Count != UnitPowerProgressionSteps)
        {
            Fail($"heroProgressionSteps must have exactly {UnitPowerProgressionSteps} steps, found {heroSteps.Count}.");
        }

        for (var index = 0; index < heroSteps.Count; index++)
        {
            if (heroSteps[index].UnitStatMultiplierPct <= 0 || heroSteps[index].AbilityPowerMultiplier <= 0)
            {
                Fail($"heroProgressionSteps step '{index}' multipliers must be positive.");
            }
        }

        var mowSteps = raw.HeroProgressionStepsMoW ?? [];
        if (mowSteps.Count != UnitPowerProgressionSteps)
        {
            Fail($"heroProgressionStepsMoW must have exactly {UnitPowerProgressionSteps} steps, found {mowSteps.Count}.");
        }

        for (var index = 0; index < mowSteps.Count; index++)
        {
            if (mowSteps[index].AbilityPowerMultiplier <= 0)
            {
                Fail($"heroProgressionStepsMoW step '{index}' abilityPowerMultiplier must be positive.");
            }

            RequireNotNegative($"heroProgressionStepsMoW step '{index}' mythicAbilityPower", mowSteps[index].MythicAbilityPower ?? 0);
        }

        // ---- modifiers and curves -------------------------------------------------------------
        var damageProfiles = raw.DamageProfileModifiers ?? new Dictionary<string, int>();
        foreach (var (damageType, modifier) in damageProfiles)
        {
            RequireNotNegative($"damageProfileModifiers '{damageType}'", modifier);
        }

        foreach (var (traitId, modifier) in raw.TraitPowerModifiers ?? new Dictionary<string, int>())
        {
            RequireNotNegative($"traitPowerModifiers '{traitId}'", modifier);
        }

        foreach (var (abilityId, modifier) in raw.AbilityPowerModifiers ?? new Dictionary<string, GameCatalogRawAbilityPowerModifier>())
        {
            RequireNotNegative($"abilityPowerModifiers '{abilityId}'", modifier.BaseMultiplier);
        }

        var active = raw.AbilityPowerCurve?.Active ?? [];
        var passive = raw.AbilityPowerCurve?.Passive ?? [];
        var relic = raw.AbilityPowerCurve?.Relic ?? [];
        foreach (var (curveName, curve) in new[] { ("active", active), ("passive", passive), ("relic", relic) })
        {
            foreach (var value in curve)
            {
                RequireNotNegative($"abilityPowerCurve.{curveName} value", value);
            }
        }

        // The cost ladder is keyed by the level raised from, so its last lvl + 1 is the highest level.
        var maxAbilityLevel = abilityCosts.Count == 0 ? 0 : abilityCosts.Max(cost => cost.Lvl) + 1;
        if (active.Count < maxAbilityLevel)
        {
            Fail($"abilityPowerCurve.active has {active.Count} levels, fewer than the highest ability level {maxAbilityLevel}.");
        }

        if (passive.Count < maxAbilityLevel)
        {
            Fail($"abilityPowerCurve.passive has {passive.Count} levels, fewer than the highest ability level {maxAbilityLevel}.");
        }

        foreach (var item in equipment.Where(item => item.IsRelic && item.Levels.Count > relic.Count))
        {
            Fail($"relic '{item.Id}' has {item.Levels.Count} levels, more than the {relic.Count} of abilityPowerCurve.relic.");
        }

        foreach (var damageType in characters
                     .SelectMany(character => new[] { character.MeleeDamage, character.RangedDamage })
                     .Concat(npcs.SelectMany(npc => new[] { npc.MeleeDamage, npc.RangedDamage }))
                     .Where(damageType => !string.IsNullOrEmpty(damageType) && !damageProfiles.ContainsKey(damageType))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            Fail($"damage type '{damageType}' has no damageProfileModifiers entry.");
        }

        // ---- per-unit entries -----------------------------------------------------------------
        var lineup = raw.Lineup ?? new Dictionary<string, GameCatalogRawUnitPowerEntry>();
        var upgradeStats = upgrades
            .GroupBy(upgrade => upgrade.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Stat, StringComparer.OrdinalIgnoreCase);

        foreach (var mow in mows)
        {
            if (!lineup.TryGetValue(mow.Id, out var entry))
            {
                Fail($"has no lineup entry for MoW '{mow.Id}'.");
            }
            else if (entry.PowerMultiplier is { } powerMultiplier)
            {
                RequireNotNegative($"unit '{mow.Id}' powerMultiplier", powerMultiplier);
            }
        }

        foreach (var character in characters)
        {
            if (!lineup.TryGetValue(character.Id, out var entry))
            {
                Fail($"has no lineup entry for character '{character.Id}'.");
                continue;
            }

            if (entry.PowerMultiplier is { } powerMultiplier)
            {
                RequireNotNegative($"unit '{character.Id}' powerMultiplier", powerMultiplier);
            }

            var rows = entry.UpgradesStatIncrease ?? [];
            if (rows.Count < character.RankUpUpgrades.Count)
            {
                Fail($"unit '{character.Id}' upgradesStatIncrease has {rows.Count} rows, fewer than its {character.RankUpUpgrades.Count} rankUpUpgrades rows.");
            }

            for (var index = 0; index < character.RankUpUpgrades.Count; index++)
            {
                var rankUp = character.RankUpUpgrades[index];
                if (index < rows.Count && rows[index].Count != rankUp.UpgradeIds.Count)
                {
                    Fail($"unit '{character.Id}' rank '{rankUp.Rank}' upgradesStatIncrease has {rows[index].Count} values, but the rank has {rankUp.UpgradeIds.Count} upgrade ids.");
                }

                if (index < rows.Count)
                {
                    foreach (var value in rows[index])
                    {
                        RequireNotNegative($"unit '{character.Id}' rank '{rankUp.Rank}' upgradesStatIncrease value", value);
                    }
                }

                foreach (var upgradeId in rankUp.UpgradeIds.Where(upgradeId =>
                             !upgradeStats.TryGetValue(upgradeId, out var stat) || !UnitPowerUpgradeStats.Contains(stat)))
                {
                    Fail($"unit '{character.Id}' rank '{rankUp.Rank}' upgrade '{upgradeId}' is not a catalog upgrade raising Health, Damage or Armour.");
                }
            }
        }
    }
}
