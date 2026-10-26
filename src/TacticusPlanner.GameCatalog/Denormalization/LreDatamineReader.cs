using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    // Raw paths of the datamine files the lres and lre-battles datasets are built from (with the quest groups and
    // tiered progress ladders shared with mythic-quests and HSE scoring).
    public const string DatamineLegendaryHeroEventsPath = "gameconfig/liveEvents/legendaryHeroEvent.json";
    public const string DatamineBattleSetsPath = "gameconfig/battles/battleSets.json";
    public const string DatamineShopSegmentsPath = "gameconfig/shop/segments.json";
    public const string DatamineShopProductsPath = "gameconfig/shop/products.json";
    public const string DatamineChestsPath = "gameconfig/loot/chests.json";
    public const string DatamineHeroProgressionStepsPerUnitPath = "gameconfig/units/heroProgressionStepsPerUnit.json";

    private const string LreAcingObjective = "Acing";
    private const string LreCurrencyPrefix = "legendaryEventCurrency:";
    private const string LreShardsPrefix = "legendaryEventShards:";
    private const string LreShardsCost = "legendaryEventShards";

    [GeneratedRegex(@"^legendary_hero_event_(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex LegendaryHeroEventNameRegex();

    // Battle objective type -> the served restriction filter kind and whether it excludes. Has/HasNoRangedAttack
    // carry no target; they are served as V1's AttackType "Ranged" filter.
    private static readonly Dictionary<string, (string Kind, bool Exclude)> LreObjectiveFilters =
        new(StringComparer.Ordinal)
        {
            ["Faction"] = ("Faction", false),
            ["DamageType"] = ("DamageType", false),
            ["NotDamageType"] = ("DamageType", true),
            ["Trait"] = ("Trait", false),
            ["NotTrait"] = ("Trait", true),
            ["MaxHits"] = ("MaxHits", false),
            ["MinHits"] = ("MinHits", false),
            ["HasRangedAttack"] = ("AttackType", false),
            ["HasNoRangedAttack"] = ("AttackType", true),
        };

    private const string LreAttackTypeTarget = "Ranged";

    // The (rarity, stars) progression steps whose event-shard ascension cost fills each served progression field.
    private static readonly (string Rarity, int Stars)[] LreProgressionSteps =
    [
        ("Legendary", 8),
        ("Legendary", 9),
        ("Legendary", 10),
        ("Legendary", 11),
        ("Mythic", 11),
        ("Mythic", 12),
    ];

    /// <summary>
    /// Reads every curated Legendary Release Event from the datamine into the raw record the lres and lre-battles
    /// datasets are built from, keyed <c>lres-{unit id in lower case}</c>. Battles, restriction points and filters,
    /// kill points, missions, the points ladder, chest costs, shards per chest and the progression shard costs come
    /// from the game; display names, run starts, battle completion points and the allowed-units filter are curated.
    /// </summary>
    /// <exception cref="InvalidOperationException">An event, battle set, ladder or mission cannot be read.</exception>
    public static Dictionary<string, GameCatalogLre> ReadDatamineLres(
        IReadOnlyDictionary<string, GameCatalogLreCuration> curation,
        DatamineLreSources sources,
        JsonSerializerOptions options)
    {
        var lres = new Dictionary<string, GameCatalogLre>(StringComparer.Ordinal);
        foreach (var (unitId, curated) in curation)
        {
            lres[$"{GameCatalogDatasets.LresPrefix}-{unitId.ToLowerInvariant()}"] = ReadLre(unitId, curated, sources, options);
        }

        return lres;
    }

    private static GameCatalogLre ReadLre(
        string unitId, GameCatalogLreCuration curated, DatamineLreSources sources, JsonSerializerOptions options)
    {
        var lre = sources.Events.FirstOrDefault(candidate => candidate.EventName == curated.Event)
            ?? throw LreError(unitId, $"names event '{curated.Event}' the game does not have");
        if (lre.UnlockableHero.HeroId != unitId)
        {
            throw LreError(unitId, $"names event '{curated.Event}', which unlocks '{lre.UnlockableHero.HeroId}'");
        }

        var number = LegendaryHeroEventNameRegex().Match(lre.EventName);
        if (!number.Success)
        {
            throw LreError(unitId, $"event '{lre.EventName}' has no event number");
        }

        GameCatalogLreTrack Track(int laneIndex, string trackKey, GameCatalogLreTrackCuration track)
        {
            var lane = lre.Lanes.SingleOrDefault(candidate => candidate.LaneIndex == laneIndex)
                ?? throw LreError(unitId, $"has no lane {laneIndex} ({trackKey})");
            var battles = sources.BattleSets.TryGetValue(lane.BattleSetId, out var set)
                ? set.Deserialize<List<DatamineLreBattle>>(options) ?? []
                : throw LreError(unitId, $"{trackKey} names battle set '{lane.BattleSetId}' the game does not have");
            return ReadLreTrack(unitId, trackKey, track, battles);
        }

        var alpha = Track(1, "alpha", curated.Alpha);
        var beta = Track(2, "beta", curated.Beta);
        var gamma = Track(3, "gamma", curated.Gamma);
        GameCatalogLreTrack[] tracks = [alpha, beta, gamma];
        if (tracks.Any(track => track.Battles.Count != alpha.Battles.Count))
        {
            throw LreError(unitId, "has tracks with different battle counts");
        }

        if (tracks.Any(track => track.UnitsRestrictions.Count != alpha.UnitsRestrictions.Count))
        {
            throw LreError(unitId, "has tracks with different restriction counts");
        }

        var (regular, premium) = ReadLreMissions(unitId, lre, sources.QuestGroups, options);
        var (chests, shardsPerChest) = ReadLreChests(unitId, lre, sources, options);

        return new GameCatalogLre(
            $"{DatamineLegendaryHeroEventsPath}#{lre.EventName}",
            int.Parse(number.Groups[1].ValueSpan, CultureInfo.InvariantCulture),
            unitId,
            curated.Name,
            curated.RunStartDatesUtc ?? [],
            alpha.Battles.Count,
            alpha.UnitsRestrictions.Count,
            regular,
            premium,
            alpha,
            beta,
            gamma,
            ReadLrePointsMilestones(unitId, lre, sources.Ladders),
            chests,
            shardsPerChest,
            ReadLreProgression(unitId, sources.ProgressionSteps));
    }

    private static GameCatalogLreTrack ReadLreTrack(
        string unitId, string trackKey, GameCatalogLreTrackCuration curated, List<DatamineLreBattle> battles)
    {
        if (battles.Count == 0)
        {
            throw LreError(unitId, $"{trackKey} has no battles");
        }

        // Every battle starts with its Acing (defeat all) score; the restrictions after it are the same in every
        // battle of the track.
        var restrictions = battles[0].Objectives.Skip(1).ToArray();
        foreach (var battle in battles)
        {
            if (battle.Objectives.Count == 0 || battle.Objectives[0].ObjectiveType != LreAcingObjective)
            {
                throw LreError(unitId, $"{trackKey} battle {battle.BattleNr} does not start with an {LreAcingObjective} objective");
            }

            if (!battle.Objectives.Skip(1).SequenceEqual(restrictions))
            {
                throw LreError(unitId, $"{trackKey} battle {battle.BattleNr} has restrictions that differ from battle {battles[0].BattleNr}");
            }
        }

        if (curated.RestrictionNames.Count != restrictions.Length)
        {
            throw LreError(unitId, $"{trackKey} curates {curated.RestrictionNames.Count} restriction names for {restrictions.Length} restrictions");
        }

        if (curated.BattlesPoints.Count != battles.Count)
        {
            throw LreError(unitId, $"{trackKey} curates {curated.BattlesPoints.Count} battle points for {battles.Count} battles");
        }

        return new GameCatalogLreTrack(
            curated.Name,
            curated.Enemies,
            battles[0].Objectives[0].Score,
            curated.BattlesPoints,
            battles.Select(battle => battle.Objectives[0].Score).ToArray(),
            curated.AllowedUnitsFilter,
            restrictions.Select((objective, index) =>
            {
                if (!LreObjectiveFilters.TryGetValue(objective.ObjectiveType, out var filter))
                {
                    throw LreError(unitId, $"{trackKey} has unknown restriction type '{objective.ObjectiveType}'");
                }

                var target = filter.Kind == "AttackType" ? LreAttackTypeTarget : objective.ObjectiveTarget;
                if (string.IsNullOrEmpty(target))
                {
                    throw LreError(unitId, $"{trackKey} restriction '{objective.ObjectiveType}' has no target");
                }

                return new GameCatalogLreRestriction(
                    curated.RestrictionNames[index],
                    objective.Score,
                    index,
                    new GameCatalogLreFilter(filter.Kind, target, filter.Exclude));
            }).ToArray(),
            battles.Select(battle => new GameCatalogLreBattle(
                battle.BoardId,
                battle.BattleNr,
                battle.Power,
                battle.Tier,
                battle.DisallowedFactions ?? [],
                battle.Waves.Select(wave => new GameCatalogLreWave(
                    wave.Round,
                    wave.Power,
                    ReadLreArmy(unitId, trackKey, battle.BattleNr, wave.Army))).ToArray())).ToArray());
    }

    // The army lists one entry per spawned enemy; the served wave counts each npc id and ladder index once, in
    // order of first appearance.
    private static GameCatalogLreEnemy[] ReadLreArmy(string unitId, string trackKey, int battleNr, IReadOnlyList<string> army)
    {
        var enemies = new List<GameCatalogLreEnemy>();
        foreach (var entry in army)
        {
            var separator = entry.LastIndexOf(':');
            if (separator <= 0
                || !int.TryParse(entry.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var ladderIndex))
            {
                throw LreError(unitId, $"{trackKey} battle {battleNr} has enemy '{entry}' without a progression index");
            }

            var id = entry[..separator];
            var existing = enemies.FindIndex(enemy => enemy.Id == id && enemy.LadderIndex == ladderIndex);
            if (existing < 0)
            {
                enemies.Add(new GameCatalogLreEnemy(id, ladderIndex, 1));
            }
            else
            {
                enemies[existing] = enemies[existing] with { Count = enemies[existing].Count + 1 };
            }
        }

        return [.. enemies];
    }

    private static (string[] Regular, string[] Premium) ReadLreMissions(
        string unitId, DatamineLegendaryHeroEvent lre, IReadOnlyDictionary<string, JsonElement> questGroups, JsonSerializerOptions options)
    {
        var groups = lre.QuestGroups.Select(name => questGroups.TryGetValue(name, out var group)
                ? group.Deserialize<DatamineLreQuestGroup>(options) ?? throw LreError(unitId, $"quest group '{name}' is empty")
                : throw LreError(unitId, $"names quest group '{name}' the game does not have"))
            .ToArray();

        string[] Missions(string questType) =>
            groups.SingleOrDefault(group => group.QuestType == questType) is { } group
                ? group.Quests.Select(quest => string.Join(", ", quest.Tasks.Select(task => LreTaskText(unitId, quest.Name, task)))).ToArray()
                : throw LreError(unitId, $"has no single '{questType}' quest group");

        return (Missions("standard"), Missions("premium"));
    }

    // Faction and trait ids -> their mission-text labels: the reverse of the parser's alias tables, so every
    // generated line parses back to the task it came from. Nested so the alias tables (initialized in another part
    // of this partial class) are set before they are reversed.
    private static class LreMissionLabels
    {
        public static readonly Dictionary<string, string> Factions =
            LreMissionFactionAliases.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

        public static readonly Dictionary<string, string> Traits =
            LreMissionTraitAliases.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
    }

    // One task's English mission text, as V1's questsToMissionText wrote it.
    private static string LreTaskText(string unitId, string questName, DatamineLreQuestTask task)
    {
        var target = task.Target;
        var plural = target == 1 ? string.Empty : "s";

        string Parameter(string name) =>
            task.TaskParameters?.GetValueOrDefault(name) is { Length: > 0 } value
                ? value
                : throw LreError(unitId, $"quest '{questName}' task '{task.Name}' has no '{name}' parameter");

        string Label(Dictionary<string, string> labels, string name, string kind) =>
            labels.TryGetValue(Parameter(name), out var label)
                ? label
                : throw LreError(unitId, $"quest '{questName}' names {kind} '{Parameter(name)}' with no mission label (add it to the {kind} alias table)");

        var thousands = (target / 1000m).ToString(CultureInfo.InvariantCulture) + "k";
        return task.Name switch
        {
            "DefeatWaves" => $"Defeat {target} waves of enemies in any wave-based game mode",
            "DealDamage" => $"Deal {target} damage",
            "DealAbilityDamage" => $"Deal {thousands} damage with Abilities",
            "DealDamageOfProfileType" => $"Deal {thousands} {Parameter("damageProfileType")} damage",
            "SlayEnemiesOfFaction" => $"Defeat {target} {Label(LreMissionLabels.Factions, "factionId", "faction")}",
            "SlayEnemiesOfTrait" => $"Defeat {target} {Label(LreMissionLabels.Traits, "trait", "trait")} enemies",
            "UseAbilityWithFactionTrait" => $"Use abilities {target} times with {Parameter("factionTrait")} units",
            "WinBattlesWithoutUsingSummons" => $"Win {target} battle{plural} without Summoning any units",
            "SlayEnemiesWithFactionTrait" => $"Defeat {target} enemies with {Parameter("factionTrait")} units",
            "WinBattlesWithoutTrait" => $"Win {target} battle{plural} without deploying any {Label(LreMissionLabels.Traits, "trait", "trait")} characters",
            "SlayEnemiesWithDamageProfile" => $"Defeat {target} enemies with {Parameter("damageProfileType")} Damage",
            _ => throw LreError(unitId, $"quest '{questName}' has unknown task '{task.Name}'"),
        };
    }

    private static GameCatalogLrePointsMilestone[] ReadLrePointsMilestones(
        string unitId, DatamineLegendaryHeroEvent lre, IReadOnlyDictionary<string, List<DatamineTieredProgressReward>> ladders) =>
        ladders.TryGetValue(lre.RewardProgressId, out var ladder)
            ? ladder.Select((step, index) => new GameCatalogLrePointsMilestone(
                index + 1,
                step.RequiredProgress,
                step.ChestRewardId.StartsWith(LreCurrencyPrefix, StringComparison.Ordinal)
                && int.TryParse(step.ChestRewardId.AsSpan(LreCurrencyPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var payout)
                    ? payout
                    : throw LreError(unitId, $"points milestone {index + 1} pays '{step.ChestRewardId}', not event currency"))).ToArray()
            : throw LreError(unitId, $"names points ladder '{lre.RewardProgressId}' the game does not have");

    private static (GameCatalogLreChestsMilestone[] Chests, int ShardsPerChest) ReadLreChests(
        string unitId, DatamineLegendaryHeroEvent lre, DatamineLreSources sources, JsonSerializerOptions options)
    {
        var segment = sources.ShopSegments.SingleOrDefault(candidate => candidate.SegmentId == lre.ShopSegmentId)
            ?? throw LreError(unitId, $"names shop segment '{lre.ShopSegmentId}' the game does not have");
        if (segment.ProductSets.Count != 1)
        {
            throw LreError(unitId, $"shop segment '{segment.SegmentId}' has {segment.ProductSets.Count} product sets, not one");
        }

        var products = segment.ProductSets[0].Products.Select(id => sources.Products.TryGetValue(id, out var product)
                ? product.Deserialize<DatamineChestProduct>(options) ?? throw LreError(unitId, $"product '{id}' is empty")
                : throw LreError(unitId, $"names product '{id}' the game does not have"))
            .ToArray();
        if (products.FirstOrDefault(product => product.Cost.Type != "legendaryEventCurrency") is { } unpaid)
        {
            throw LreError(unitId, $"chest '{unpaid.ChestId}' costs '{unpaid.Cost.Type}', not event currency");
        }

        var shards = products
            .SelectMany(product => sources.Chests.TryGetValue(product.ChestId, out var chest)
                ? chest.Deserialize<List<DatamineChest>>(options) ?? []
                : throw LreError(unitId, $"names chest '{product.ChestId}' the game does not have"))
            .SelectMany(chest => chest.Rewards)
            .Where(reward => reward.StartsWith(LreShardsPrefix, StringComparison.Ordinal))
            .Select(reward => int.Parse(reward.AsSpan(LreShardsPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();
        if (shards.Length != 1)
        {
            throw LreError(unitId, $"chests grant {shards.Length} different event shard amounts, not one");
        }

        return (
            products.Select((product, index) => new GameCatalogLreChestsMilestone(index + 1, product.Cost.Amount)).ToArray(),
            shards[0]);
    }

    private static GameCatalogLreProgression ReadLreProgression(
        string unitId, IReadOnlyDictionary<string, List<DatamineUnitProgressionStep>> progressionSteps)
    {
        var steps = progressionSteps.TryGetValue(unitId, out var unitSteps)
            ? unitSteps
            : throw LreError(unitId, "has no progression steps in the game");
        var costs = LreProgressionSteps.Select(wanted =>
                steps.FirstOrDefault(step => step.Rarity == wanted.Rarity && step.Stars == wanted.Stars)?.AscensionCosts
                    ?.GetValueOrDefault(LreShardsCost) is int cost and > 0
                    ? cost
                    : throw LreError(unitId, $"has no event shard cost for {wanted.Rarity} {wanted.Stars} stars"))
            .ToArray();

        return new GameCatalogLreProgression(costs[0], costs[1], costs[2], costs[3], costs[4], costs[5]);
    }

    private static InvalidOperationException LreError(string unitId, string problem) =>
        new($"GameCatalog dataset '{GameCatalogDatasets.Lres}': LRE '{unitId}' {problem}.");
}
