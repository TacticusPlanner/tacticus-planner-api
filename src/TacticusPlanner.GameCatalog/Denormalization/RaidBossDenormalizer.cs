using System.Globalization;
using System.Text.RegularExpressions;

using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    // Boss vs. prime classification from the raw unit-set key pattern (ported from V1's
    // guild-boss.service.ts BOSS_SET_RE / PRIME_SET_RE). A key matching neither (field npcs, loot objects)
    // is not a top-level entry but is still resolvable for an encounter's fieldNpcIds.
    [GeneratedRegex(@"^GuildBoss(\d+)Boss", RegexOptions.CultureInvariant)]
    private static partial Regex RaidBossKeyRegex();

    [GeneratedRegex(@"^GuildBoss(\d+)(?:MiniBoss|Minion)(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex RaidBossPrimeKeyRegex();

    public const string RaidBossKindBoss = "boss";
    public const string RaidBossKindPrime = "prime";

    /// <summary>
    /// Classifies a raw unit-set key as <see cref="RaidBossKindBoss"/> or <see cref="RaidBossKindPrime"/>,
    /// or <c>null</c> for a key that is neither (field npcs, loot objects — referenced by encounters but
    /// never a top-level served entry).
    /// </summary>
    public static string? ClassifyRaidBossUnit(string unitSetKey) =>
        RaidBossKeyRegex().IsMatch(unitSetKey) ? RaidBossKindBoss
        : RaidBossPrimeKeyRegex().IsMatch(unitSetKey) ? RaidBossKindPrime
        : null;

    /// <summary>
    /// Consolidates the raw raid-boss data into the served <c>raid-bosses</c> dataset: bosses and primes as
    /// two ordered lists (identity + progression + weapons + ability/trait ids + <c>isPrimarch</c>), the
    /// season configs keyed by id, and every encounter carrying its referenced unit-set id + progression
    /// index, field-npc ids, and resolved modifier definitions inlined. Cross-reference failures
    /// (unresolved encounter unit / modifier id, empty progression, bad encounter type) are not thrown
    /// here — <c>Validation/RaidBossValidation.cs</c> runs over the raw snapshot and fails the build.
    /// </summary>
    public static GameCatalogRaidBossesView BuildRaidBosses(GameCatalogRaidBossRawData raw)
    {
        var primarchs = new HashSet<string>(raw.Primarchs, StringComparer.Ordinal);

        var bosses = raw.UnitSets
            .Where(pair => RaidBossKeyRegex().IsMatch(pair.Key))
            .OrderBy(pair => BossNumber(pair.Key))
            .Select(pair => BuildUnit(pair.Key, pair.Value, RaidBossKindBoss, primarchs.Contains(pair.Key)))
            .ToArray();

        var primes = raw.UnitSets
            .Where(pair => RaidBossPrimeKeyRegex().IsMatch(pair.Key))
            .OrderBy(pair => BossNumber(pair.Key))
            .ThenBy(pair => PrimeIndex(pair.Key))
            .Select(pair => BuildUnit(pair.Key, pair.Value, RaidBossKindPrime, primarchs.Contains(pair.Key)))
            .ToArray();

        var seasons = raw.Seasons.ToDictionary(
            pair => pair.Key,
            pair => BuildSeason(pair.Value, raw.Modifiers),
            StringComparer.Ordinal);

        return new GameCatalogRaidBossesView(raw.Rotation.ToArray(), bosses, primes, seasons);
    }

    private static int BossNumber(string unitSetKey)
    {
        var match = RaidBossKeyRegex().Match(unitSetKey);
        if (!match.Success)
        {
            match = RaidBossPrimeKeyRegex().Match(unitSetKey);
        }

        return match.Success ? int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture) : int.MaxValue;
    }

    private static int PrimeIndex(string unitSetKey)
    {
        var match = RaidBossPrimeKeyRegex().Match(unitSetKey);
        return match.Success ? int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture) : int.MaxValue;
    }

    private static GameCatalogRaidBossView BuildUnit(
        string unitSetId, GameCatalogRaidBossRawUnitSet raw, string kind, bool isPrimarch)
    {
        var progression = raw.Stats
            .Select(stat => new GameCatalogRaidBossStatStepView(
                stat.Health, stat.Damage, stat.FixedArmor, stat.Rank, stat.StarLevel, stat.BaseRarity,
                stat.ProgressionIndex, stat.AbilityLevel, stat.RelicAbilityLevel,
                stat.BlockChance, stat.BlockDamage, stat.CritChance, stat.CritDamage))
            .ToArray();

        var weapons = raw.Weapons?
            .Select(weapon => new GameCatalogRaidBossWeaponView(weapon.Hits, weapon.DamageProfile, weapon.Range))
            .ToArray();

        return new GameCatalogRaidBossView(
            unitSetId,
            kind,
            isPrimarch,
            raw.FactionId,
            raw.Movement,
            progression,
            weapons is { Length: > 0 } ? weapons : null,
            NullIfEmpty(raw.ActiveAbilities),
            NullIfEmpty(raw.PassiveAbilities),
            NullIfEmpty(raw.RelicAbilities),
            NullIfEmpty(raw.Traits));
    }

    private static string[]? NullIfEmpty(IReadOnlyList<string>? values) =>
        values is { Count: > 0 } ? values.ToArray() : null;

    private static GameCatalogRaidBossSeasonView BuildSeason(
        GameCatalogRaidBossRawSeason raw, IReadOnlyDictionary<string, GameCatalogRaidBossRawModifier> modifiers)
    {
        var tiers = raw.Tiers
            .Select(tier => new GameCatalogRaidBossTierView(
                tier.Tier,
                tier.Sets
                    .Select(set => new GameCatalogRaidBossSetView(
                        set.Set,
                        set.ChestId,
                        set.GuildXp,
                        set.Encounters.Select(encounter => BuildEncounter(encounter, modifiers)).ToArray()))
                    .ToArray()))
            .ToArray();

        return new GameCatalogRaidBossSeasonView(raw.GuildBossSeasonConfigId, tiers);
    }

    private static GameCatalogRaidBossEncounterView BuildEncounter(
        GameCatalogRaidBossRawEncounter raw, IReadOnlyDictionary<string, GameCatalogRaidBossRawModifier> modifiers)
    {
        var fieldNpcIds = new[] { raw.Npc1Id, raw.Npc2Id }
            .Concat(raw.Enemies ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => StripProgressionSuffix(id!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var encounterModifiers = (raw.Modifiers ?? [])
            .Select(modifier =>
            {
                modifiers.TryGetValue(modifier.Modifier, out var definition);
                return new GameCatalogRaidBossEncounterModifierView(
                    modifier.HpLost,
                    modifier.Modifier,
                    definition?.Type ?? string.Empty,
                    definition?.Target ?? string.Empty,
                    definition?.Subtarget,
                    definition?.Amount ?? 0);
            })
            .ToArray();

        return new GameCatalogRaidBossEncounterView(
            raw.EncounterIndex,
            raw.GuildBossEncounterType,
            raw.BoardId,
            raw.MaxNrOfTurns,
            StripProgressionSuffix(raw.UnitId),
            ProgressionIndexFromUnitId(raw.UnitId),
            raw.BossType,
            fieldNpcIds,
            raw.DisallowedFactions?.ToArray() ?? [],
            encounterModifiers);
    }

    /// <summary>Strips a trailing <c>:N</c> progression suffix (e.g. <c>Foo:3</c> → <c>Foo</c>).</summary>
    public static string StripProgressionSuffix(string rawUnitId)
    {
        var colon = rawUnitId.LastIndexOf(':');
        return colon < 0 ? rawUnitId : rawUnitId[..colon];
    }

    /// <summary>The 1-based progression index in a <c>:N</c> suffix (1 when absent or unparseable).</summary>
    public static int ProgressionIndexFromUnitId(string rawUnitId)
    {
        var colon = rawUnitId.LastIndexOf(':');
        return colon >= 0
            && int.TryParse(rawUnitId.AsSpan(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? index
            : 1;
    }
}
