using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine file characters and MoWs are built from.</summary>
    public const string DatamineLineupPath = "gameconfig/units/lineup.json";

    /// <summary>Raw path of the datamine file NPCs are built from.</summary>
    public const string DatamineNpcPath = "gameconfig/units/npc.json";

    /// <summary>Raw path of the datamine ability table (damage profiles, MoW ability recipes).</summary>
    public const string DatamineAbilitiesPath = "gameconfig/units/abilities.json";

    // Every lineup unit and NPC carries this trait in the game; the catalog has never served it.
    private const string HeroTraitId = "Hero";

    // Faction-less loot objects in npc.json go to this bucket.
    private const string NpcObjectsFactionId = "Objects";
    private const string NpcObjectsAlliance = "Neutral";

    [GeneratedRegex(@"^damageProfile(_\d+)?$")]
    private static partial Regex DamageProfileKey();

    /// <summary>
    /// Reads the datamine <c>lineup.json</c> into per-faction unit records keyed <c>units-{faction}</c> (lowercased
    /// faction id) in key order, characters and MoWs each ordered by id. A unit whose traits contain <c>MachineOfWar</c> is a
    /// MoW: its first two active abilities are the primary and secondary, each with its <c>abilities.json</c>
    /// recipes, and its first mythic ability is the mythic one. Everything else is a character: stats, weapons
    /// (melee first, then ranged), traits without <c>Hero</c>, abilities in game order, the abilities' damage
    /// profiles (distinct, sorted), item slots and one rank-up row per <c>upgrades</c> entry from Stone1 on.
    /// </summary>
    /// <exception cref="InvalidOperationException">A unit has a weapon list, ability or rank-up list the catalog cannot read.</exception>
    public static IReadOnlyDictionary<string, GameCatalogFactionUnits> ReadDatamineUnits(
        IReadOnlyDictionary<string, DatamineLineupUnit> lineup,
        IReadOnlyDictionary<string, DatamineAbility?> abilities) =>
        lineup
            .GroupBy(pair => pair.Value.FactionId, StringComparer.Ordinal)
            .OrderBy(group => group.Key.ToLowerInvariant(), StringComparer.Ordinal)
            .ToDictionary(
                group => $"{GameCatalogDatasets.UnitsPrefix}-{group.Key.ToLowerInvariant()}",
                group =>
                {
                    var units = group.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
                    var mows = units.Where(pair => pair.Value.Traits.Contains(MachineOfWarTraitId, StringComparer.Ordinal)).ToList();
                    return new GameCatalogFactionUnits(
                        group.First().Value.GrandAllianceId,
                        group.Key,
                        group.Key,
                        units.Except(mows).Select(pair => ReadCharacter(pair.Key, pair.Value, abilities)).ToArray(),
                        mows.Select(pair => ReadMow(pair.Key, pair.Value, abilities)).ToArray());
                },
                StringComparer.Ordinal);

    /// <summary>
    /// Reads the datamine <c>npc.json</c> into per-faction NPC records keyed <c>npcs-{faction}</c> (lowercased
    /// faction id; faction-less loot objects go to <c>npcs-objects</c>, faction <c>Objects</c>, alliance
    /// <c>Neutral</c>) in key order, each ordered by id. Weapons, traits and damage profiles read as for characters; an
    /// absent weapon, movement or stat number is 0, and stat rows keep the game's order.
    /// </summary>
    /// <exception cref="InvalidOperationException">An NPC has a weapon list or ability the catalog cannot read.</exception>
    public static IReadOnlyDictionary<string, GameCatalogFactionNpcs> ReadDatamineNpcs(
        IReadOnlyDictionary<string, DatamineNpc> npcs,
        IReadOnlyDictionary<string, DatamineAbility?> abilities) =>
        npcs
            .GroupBy(pair => pair.Value.FactionId ?? NpcObjectsFactionId, StringComparer.Ordinal)
            .OrderBy(group => group.Key.ToLowerInvariant(), StringComparer.Ordinal)
            .ToDictionary(
                group => $"{GameCatalogDatasets.NpcsPrefix}-{group.Key.ToLowerInvariant()}",
                group => new GameCatalogFactionNpcs(
                    group.First().Value.GrandAllianceId ?? NpcObjectsAlliance,
                    group.Key,
                    group.Key,
                    group
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => ReadNpc(pair.Key, pair.Value, abilities))
                        .ToArray()),
                StringComparer.Ordinal);

    private static GameCatalogCharacter ReadCharacter(
        string id, DatamineLineupUnit unit, IReadOnlyDictionary<string, DatamineAbility?> abilities)
    {
        var (melee, ranged) = ReadWeapons(GameCatalogDatasets.UnitsPrefix, id, unit.Weapons);
        var passives = unit.PassiveAbilities ?? [];
        var rankUps = unit.Upgrades ?? [];
        if (rankUps.Count > Enum.GetValues<UnitRank>().Length)
        {
            throw new InvalidOperationException(
                $"Dataset '{GameCatalogDatasets.UnitsPrefix}' unit '{id}' has {rankUps.Count} rank-up rows; at most " +
                $"{Enum.GetValues<UnitRank>().Length} ranks exist.");
        }

        return new GameCatalogCharacter(
            id,
            unit.Name,
            unit.Stats.Health,
            unit.Stats.Damage,
            unit.Stats.FixedArmor,
            unit.BaseRarity,
            melee?.DamageProfile ?? "",
            melee?.Hits ?? 0,
            ranged?.DamageProfile,
            ranged?.Hits,
            ranged?.Range,
            unit.Movement ?? 0,
            WithoutHeroTrait(unit.Traits),
            DamageProfiles(GameCatalogDatasets.UnitsPrefix, id, unit.ActiveAbilities, abilities),
            unit.ActiveAbilities,
            DamageProfiles(GameCatalogDatasets.UnitsPrefix, id, passives, abilities),
            passives,
            unit.ItemSlots ?? [],
            rankUps.Select((upgradeIds, index) => new GameCatalogCharacterRankUp(((UnitRank)index).ToString(), upgradeIds)).ToArray());
    }

    private static GameCatalogMow ReadMow(
        string id, DatamineLineupUnit unit, IReadOnlyDictionary<string, DatamineAbility?> abilities)
    {
        if (unit.ActiveAbilities is not [var primary, var secondary])
        {
            throw new InvalidOperationException(
                $"Dataset 'mows' MoW '{id}' must have exactly two active abilities; found [{string.Join(", ", unit.ActiveAbilities)}].");
        }

        return new GameCatalogMow(
            id,
            unit.Name,
            MowUnitKind,
            unit.FactionId,
            unit.GrandAllianceId,
            new GameCatalogMowAbility(primary, MowRecipes(id, primary, abilities)),
            new GameCatalogMowAbility(secondary, MowRecipes(id, secondary, abilities)),
            unit.MythicAbilities is [var mythic, ..] ? new GameCatalogMowMythicAbility(mythic) : null);
    }

    private static GameCatalogRawNpc ReadNpc(
        string id, DatamineNpc npc, IReadOnlyDictionary<string, DatamineAbility?> abilities)
    {
        var (melee, ranged) = ReadWeapons(GameCatalogDatasets.NpcsPrefix, id, npc.Weapons);
        var actives = npc.ActiveAbilities ?? [];
        var passives = npc.PassiveAbilities ?? [];
        return new GameCatalogRawNpc(
            id,
            npc.Name,
            melee?.DamageProfile ?? "",
            melee?.Hits ?? 0,
            ranged?.DamageProfile,
            ranged?.Hits,
            ranged?.Range,
            npc.Movement ?? 0,
            WithoutHeroTrait(npc.Traits ?? []),
            DamageProfiles(GameCatalogDatasets.NpcsPrefix, id, actives, abilities),
            actives,
            DamageProfiles(GameCatalogDatasets.NpcsPrefix, id, passives, abilities),
            passives,
            npc.Stats
                .Select(stat => new GameCatalogNpcStat(
                    stat.AbilityLevel, stat.Damage, stat.FixedArmor, stat.Health, stat.ProgressionIndex, stat.Rank, stat.StarLevel))
                .ToArray());
    }

    private static (DatamineWeapon? Melee, DatamineWeapon? Ranged) ReadWeapons(
        string dataset, string id, IReadOnlyList<DatamineWeapon>? weapons) =>
        weapons switch
        {
            null or [] => (null, null),
            [{ Range: null } melee] => (melee, null),
            [{ Range: null } melee, { Range: not null } ranged] => (melee, ranged),
            _ => throw new InvalidOperationException(
                $"Dataset '{dataset}' unit '{id}' must have a melee weapon (no range) and at most one ranged weapon; " +
                $"found {weapons.Count} weapons."),
        };

    private static string[] WithoutHeroTrait(IReadOnlyList<string> traits) =>
        traits.Where(trait => trait != HeroTraitId).ToArray();

    // The distinct damageProfile / damageProfile_N constants of the given abilities, sorted ordinally.
    private static string[] DamageProfiles(
        string dataset, string id, IReadOnlyList<string> abilityIds, IReadOnlyDictionary<string, DatamineAbility?> abilities) =>
        abilityIds
            .SelectMany(abilityId => abilities.TryGetValue(abilityId, out var ability)
                ? (ability?.Constants ?? new Dictionary<string, string>())
                    .Where(constant => DamageProfileKey().IsMatch(constant.Key))
                    .Select(constant => constant.Value)
                : throw new InvalidOperationException(
                    $"Dataset '{dataset}' unit '{id}' has ability '{abilityId}', which abilities.json does not define."))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<IReadOnlyList<string>> MowRecipes(
        string id, string abilityId, IReadOnlyDictionary<string, DatamineAbility?> abilities) =>
        abilities.TryGetValue(abilityId, out var ability) && ability?.Upgrades is { Count: > 0 } recipes
            ? recipes
            : throw new InvalidOperationException(
                $"Dataset 'mows' MoW '{id}' ability '{abilityId}' has no recipes in abilities.json.");
}
