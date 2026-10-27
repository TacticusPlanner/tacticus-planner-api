using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    // Raw paths of the datamine files the onslaught-rewards, event-definitions (config.hse) and quest-events
    // datasets are built from.
    public const string DatamineOnslaughtPath = "gameconfig/battles/onslaught.json";
    public const string DatamineHomeScreenEventsPath = "gameconfig/liveEvents/homeScreenEvent.json";
    public const string DatamineTieredProgressRewardsPath = "gameconfig/loot/tieredProgressRewards.json";
    public const string DatamineLinearHeroEventsPath = "gameconfig/liveEvents/linearHeroEvent.json";

    private static readonly string[] OnslaughtSectors = ["Stone", "Iron", "Bronze", "Silver", "Gold", "Diamond", "Adamantine"];
    private const int OnslaughtTiersPerSector = 3;

    // The hero progression index whose reward row gives each served shard range: the top step of Common, Uncommon,
    // Rare, Epic and Legendary before blue stars (regular shards), and the top Mythic step (mythic shards).
    private static readonly int[] OnslaughtRegularShardSteps = [2, 5, 8, 11, 14];
    private const int OnslaughtMythicShardStep = 19;
    private const string OnslaughtShardsPrefix = "shards_[heroId]:";
    private const string OnslaughtMythicShardsPrefix = "mythicShards_[heroId]:";

    private static readonly string[] OnslaughtBadgeRarities = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];

    [GeneratedRegex(@"^abilityToken(Common|Uncommon|Rare|Epic|Legendary|Mythic)_(?:Imperial|Chaos|Xenos)(?::(\d+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex OnslaughtBadgeRegex();

    [GeneratedRegex(@"^(\d+)(?:-(\d+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex RewardRangeRegex();

    /// <summary>
    /// Reads the datamine <c>onslaught.json</c> into one reward record per sector tier (Stone-1 .. Adamantine-3).
    /// <c>regular</c> is the shard range of the top Common, Uncommon, Rare, Epic and Legendary (pre-blue-star) step,
    /// <c>mythic</c> the mythic-shard range of the top Mythic step, and <c>badges</c> the ability badges the tier's
    /// rounds award, summed per rarity.
    /// </summary>
    /// <exception cref="InvalidOperationException">A tier, reward row or badge cannot be read.</exception>
    public static IReadOnlyList<GameCatalogOnslaughtReward> ReadDatamineOnslaughtRewards(DatamineOnslaught onslaught) =>
        onslaught.Tiers
            .Select(tier => tier.Tier)
            .Order()
            .Select(tier =>
            {
                var sectorIndex = (tier - 1) / OnslaughtTiersPerSector;
                if (tier < 1 || sectorIndex >= OnslaughtSectors.Length)
                {
                    throw OnslaughtError($"tier {tier} is outside the {OnslaughtSectors.Length} sectors");
                }

                var sector = OnslaughtSectors[sectorIndex];
                var tierInSector = ((tier - 1) % OnslaughtTiersPerSector) + 1;
                var rows = onslaught.HonorYourHeroesRewards
                    .Where(row => row.Tier == tier)
                    .OrderBy(row => row.MaxProgressionIndex)
                    .ToArray();

                return new GameCatalogOnslaughtReward(
                    $"{sector}-{tierInSector}",
                    sector,
                    tierInSector,
                    OnslaughtRegularShardSteps.Select(step => OnslaughtShards(tier, rows, step, OnslaughtShardsPrefix)).ToArray(),
                    OnslaughtShards(tier, rows, OnslaughtMythicShardStep, OnslaughtMythicShardsPrefix),
                    OnslaughtBadges(tier, onslaught.TierWaves));
            })
            .ToArray();

    private static GameCatalogRewardRange OnslaughtShards(
        int tier, DatamineOnslaughtHeroRewards[] rows, int step, string prefix)
    {
        var reward = rows.FirstOrDefault(row => row.MaxProgressionIndex >= step)?.Rewards
            .FirstOrDefault(reward => reward.StartsWith(prefix, StringComparison.Ordinal));
        var match = reward is null ? null : RewardRangeRegex().Match(reward[prefix.Length..]);
        if (match is not { Success: true })
        {
            throw OnslaughtError($"tier {tier} has no '{prefix}min-max' reward for progression index {step}");
        }

        var min = int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        var max = match.Groups[2].Success ? int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture) : min;
        return new GameCatalogRewardRange(min, max);
    }

    private static GameCatalogAmountByRarity[] OnslaughtBadges(int tier, IReadOnlyList<DatamineOnslaughtWave> waves)
    {
        var amountByRarity = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var round in waves.Where(wave => wave.Tier == tier && wave.Rewards is { Count: > 0 }).GroupBy(wave => wave.Round))
        {
            var badges = round
                .SelectMany(wave => wave.Rewards!.Values)
                .Select(reward => OnslaughtBadgeRegex().Match(reward) is { Success: true } match
                    ? (Rarity: match.Groups[1].Value,
                        Amount: match.Groups[2].Success ? int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture) : 1)
                    : throw OnslaughtError($"tier {tier} round {round.Key} reward '{reward}' is not an ability badge"))
                .Distinct()
                .ToArray();
            if (badges.Length != 1)
            {
                throw OnslaughtError($"tier {tier} round {round.Key} waves give different badges");
            }

            amountByRarity[badges[0].Rarity] = amountByRarity.GetValueOrDefault(badges[0].Rarity) + badges[0].Amount;
        }

        return OnslaughtBadgeRarities
            .Where(amountByRarity.ContainsKey)
            .Select(rarity => new GameCatalogAmountByRarity(rarity, amountByRarity[rarity]))
            .ToArray();
    }

    private static InvalidOperationException OnslaughtError(string detail) =>
        new($"Dataset '{GameCatalogDatasets.OnslaughtRewards}': {detail}.");

    // Game event name (without its `_tier_high` / `_tier_mid` / `_tier_low` suffix) -> the curated event definition,
    // variant key and scope it scores.
    private static readonly Dictionary<string, (string DefinitionId, string VariantKey, string Scope)> HseSources =
        new(StringComparer.Ordinal)
        {
            ["trait_boost_psyker"] = ("hse-trait-boost-psyker", "default", "Personal"),
            ["trait_boost_flying"] = ("hse-trait-boost-flying", "default", "Personal"),
            ["hse_trait_boost_rapid_assault"] = ("hse-trait-boost-rapid-assault", "default", "Personal"),
            ["hse_trait_boost_terminator_armour"] = ("hse-terminator-boost", "default", "Personal"),
            ["for_the_emperor"] = ("hse-for-the-emperor", "default", "Personal"),
            ["for_the_dark_gods"] = ("hse-for-the-dark-gods", "default", "Personal"),
            ["faction_boost_dark_angels"] = ("hse-faction-boost", "DarkAngels", "Personal"),
            ["faction_boost_space_wolves"] = ("hse-faction-boost", "SpaceWolves", "Personal"),
            ["faction_boost_adeptus_mechanicus"] = ("hse-faction-boost", "AdeptusMechanicus", "Personal"),
            ["faction_focus"] = ("hse-faction-focus", "default", "Personal"),
            ["hse_faction_focus_october_2026"] = ("hse-faction-focus", "october-2026", "Personal"),
            ["defeat_waves"] = ("hse-defeat-waves", "default", "Personal"),
            ["defeat_waves_2"] = ("hse-defeat-waves", "run-2", "Personal"),
            ["hse_11th_edition_week1"] = ("hse-11th-edition-week-1", "default", "Personal"),
            ["global_hse_11th_edition_week1_blood_angels"] = ("hse-11th-edition-week-1", "global-blood-angels", "Global"),
            ["global_hse_11th_edition_week1_orks"] = ("hse-11th-edition-week-1", "global-orks", "Global"),
            ["hse_11th_edition_week2"] = ("hse-11th-edition-week-2", "default", "Personal"),
            ["global_hse_11th_edition_week2_blood_angels"] = ("hse-11th-edition-week-2", "global-blood-angels", "Global"),
            ["global_hse_11th_edition_week2_orks"] = ("hse-11th-edition-week-2", "global-orks", "Global"),
            ["hse_11th_edition_week3"] = ("hse-11th-edition-week-3", "default", "Personal"),
            ["global_hse_11th_edition_week3_blood_angels"] = ("hse-11th-edition-week-3", "global-blood-angels", "Global"),
            ["global_hse_11th_edition_week3_orks"] = ("hse-11th-edition-week-3", "global-orks", "Global"),
            ["global_hse_operation_imperator"] = ("hse-global-operation-imperator", "default", "Global"),
            ["conflict_operations"] = ("hse-global-conflict-operations", "default", "Global"),
            ["arsenal_of_war"] = ("hse-arsenal-of-war", "default", "Personal"),
            ["squig_smash"] = ("hse-squig-smash", "default", "Personal"),
            ["machine_hunt"] = ("hse-machine-hunt", "default", "Personal"),
            ["warp_surge"] = ("hse-warp-surge", "default", "Personal"),
            ["rarity_training_rush"] = ("hse-training-rush", "default", "Personal"),
            ["kill_tyranids"] = ("hse-purge-order", "default", "Personal"),
        };

    private static readonly string[] HseTierOrder = ["high", "mid", "low", "default"];

    // The game's mode ids -> the catalog names, where they differ.
    private static readonly Dictionary<string, string> HseGameModeNames = new(StringComparer.Ordinal)
    {
        ["PvP"] = "Arena",
        ["SyncPVP"] = "TournamentArena",
        ["Waves"] = "Onslaught",
        ["TreasureBeach"] = "SalvageRun",
    };

    private static readonly Dictionary<string, string> HseTrackerKinds = new(StringComparer.Ordinal)
    {
        ["killUnits"] = "KillUnits",
        ["defeatWaves"] = "DefeatWaves",
        ["deployedUnitsOfFactionMajority"] = "DeployedUnitsOfFactionMajority",
        ["fullLineUpOfFaction"] = "FullLineUpOfFaction",
        ["donateUpgradeToGuildMember"] = "DonateUpgradeToGuildMember",
        ["donateIntelToGuildProject"] = "DonateIntelToGuildProject",
        ["dispatchExpedition"] = "DispatchExpedition",
    };

    private const string HseRaidBattlesTracker = "raidBattles";
    private const string HseUnitXpModifier = "unitXp";
    private const string HseEnvironmentAbilityModifier = "battleEnvironmentAbility";
    private const string HseStatBoostAbilityPrefix = "EnvStatBoost:";

    [GeneratedRegex("^(.+)_tier_(high|mid|low)$", RegexOptions.CultureInvariant)]
    private static partial Regex HseTierEventNameRegex();

    /// <summary>
    /// Reads the HSE scoring of every curated definition from the datamine <c>homeScreenEvent.json</c>: each game event
    /// (its power tiers grouped by event name) becomes one variant of its definition, with each tier's milestone ladder
    /// and repeat from <c>tieredProgressRewards.json</c>, and the trackers and modifiers shared by its tiers. A tier whose
    /// ladder the game no longer has is skipped; game events no definition scores are ignored. Raid-battle trackers
    /// (campaign sweep, covered by a kill tracker with equal points) and non-stat-boost environment abilities are not
    /// served.
    /// </summary>
    /// <exception cref="InvalidOperationException">An event cannot be mapped to the served scoring shape.</exception>
    public static IReadOnlyList<GameCatalogHseScoring> ReadDatamineHseScoring(
        IReadOnlyList<DatamineHomeScreenEvent> events,
        IReadOnlyDictionary<string, List<DatamineTieredProgressReward>> ladders,
        IReadOnlyDictionary<string, DatamineAbility?> abilities)
    {
        var tiersByEvent = new Dictionary<string, Dictionary<string, DatamineHomeScreenEvent>>(StringComparer.Ordinal);
        foreach (var hse in events)
        {
            var match = HseTierEventNameRegex().Match(hse.EventName);
            var (name, tier) = match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : (hse.EventName, "default");
            if (!tiersByEvent.TryAdd(name, new Dictionary<string, DatamineHomeScreenEvent>(StringComparer.Ordinal) { [tier] = hse })
                && !tiersByEvent[name].TryAdd(tier, hse))
            {
                throw HseError(name, $"defines tier '{tier}' twice");
            }
        }

        var missing = HseSources.Keys.Where(name => !tiersByEvent.ContainsKey(name)).ToArray();
        if (missing.Length > 0)
        {
            throw HseError(missing[0], "is not in homeScreenEvent.json");
        }

        return HseSources
            .Select(source => (source.Value.DefinitionId,
                Variant: ReadHseVariant(source.Key, source.Value.VariantKey, source.Value.Scope, tiersByEvent[source.Key], ladders, abilities)))
            .GroupBy(entry => entry.DefinitionId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new GameCatalogHseScoring(
                group.Key,
                group.Select(entry => entry.Variant)
                    .OrderBy(variant => variant.VariantKey != "default")
                    .ThenBy(variant => variant.VariantKey, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private static GameCatalogHseVariant ReadHseVariant(
        string name,
        string variantKey,
        string scope,
        Dictionary<string, DatamineHomeScreenEvent> tiers,
        IReadOnlyDictionary<string, List<DatamineTieredProgressReward>> ladders,
        IReadOnlyDictionary<string, DatamineAbility?> abilities)
    {
        var keys = HseTierOrder.Where(tiers.ContainsKey).ToArray();
        var first = tiers[keys[0]];
        // An anonymous object, not a tuple: System.Text.Json skips tuple fields, so every tuple serializes to "{}".
        static string Shared(DatamineHomeScreenEvent hse) => JsonSerializer.Serialize(new { hse.Trackers, hse.Modifiers });
        var firstShared = Shared(first);
        if (keys.Skip(1).FirstOrDefault(key => Shared(tiers[key]) != firstShared) is { } differing)
        {
            throw HseError(name, $"tier '{differing}' trackers or modifiers differ from tier '{keys[0]}'");
        }

        var servedTiers = keys
            .Where(key => ladders.ContainsKey(tiers[key].RewardProgressId))
            .Select(key => ReadHseTier(name, key, ladders[tiers[key].RewardProgressId]))
            .ToArray();

        return new GameCatalogHseVariant(
            variantKey,
            scope,
            servedTiers,
            (first.Trackers ?? [])
                .Where(tracker => tracker.Type != HseRaidBattlesTracker)
                .Select(tracker => ReadHseTracker(name, tracker))
                .ToArray(),
            (first.Modifiers ?? [])
                .Select(modifier => ReadHseModifier(name, modifier, abilities))
                .OfType<GameCatalogHseModifier>()
                .ToArray());
    }

    private static GameCatalogHseTier ReadHseTier(string name, string tier, List<DatamineTieredProgressReward> ladder)
    {
        var milestones = new List<GameCatalogHseMilestone>();
        GameCatalogHseRepeat? repeat = null;
        foreach (var step in ladder)
        {
            var (rewardId, quantity) = HseReward(name, step.ChestRewardId);
            if (step.Endless == true)
            {
                repeat = new GameCatalogHseRepeat(step.RequiredProgress, rewardId, quantity, step.EndlessCap);
            }
            else
            {
                milestones.Add(new GameCatalogHseMilestone(step.RequiredProgress, rewardId, quantity));
            }
        }

        return new GameCatalogHseTier(char.ToUpperInvariant(tier[0]) + tier[1..], milestones, repeat);
    }

    private static (string RewardId, int Quantity) HseReward(string name, string chestRewardId)
    {
        var colon = chestRewardId.IndexOf(':', StringComparison.Ordinal);
        if (colon == -1)
        {
            return (chestRewardId, 1);
        }

        return int.TryParse(chestRewardId.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
            ? (chestRewardId[..colon], quantity)
            : throw HseError(name, $"reward '{chestRewardId}' has no whole quantity");
    }

    private static GameCatalogHseTracker ReadHseTracker(string name, DatamineHseTracker tracker) =>
        new(
            HseTrackerKinds.TryGetValue(tracker.Type, out var kind) ? kind : throw HseError(name, $"has unknown tracker '{tracker.Type}'"),
            tracker.Points,
            tracker.PointsByRarity,
            HseRestriction(name, tracker.GameModeRestrictions, HseGameModeName),
            HseUnitFilter(name, tracker.TraitRestrictions, tracker.FactionRestrictions, tracker.UnitId is { Length: > 0 } unitId ? [unitId] : null),
            HseUnitFilter(name, tracker.PlayerTraitRestrictions, tracker.PlayerFactionRestrictions, null));

    private static GameCatalogHseModifier? ReadHseModifier(
        string name, DatamineHseModifier modifier, IReadOnlyDictionary<string, DatamineAbility?> abilities)
    {
        var gameModes = HseRestriction(name, modifier.GameModeRestrictions, HseGameModeName);
        if (modifier.Type == HseUnitXpModifier)
        {
            if ((modifier.Operator ?? "multiply") != "multiply")
            {
                throw HseError(name, $"has unitXp operator '{modifier.Operator}'");
            }

            var scale = modifier.PercentageModifier == true ? 100d : 1d;
            return new GameCatalogHseModifier(
                "UnitXp",
                modifier.Modifier / scale,
                modifier.Modifiers?.ToDictionary(pair => pair.Key, pair => pair.Value / scale, StringComparer.Ordinal),
                modifier.SameFactionBonus == true,
                null,
                null,
                null,
                gameModes,
                HseUnitFilter(name, modifier.TraitRestrictions, modifier.PlayerFactionRestrictions, null));
        }

        if (modifier.Type != HseEnvironmentAbilityModifier)
        {
            throw HseError(name, $"has unknown modifier '{modifier.Type}'");
        }

        // Other environment abilities (Squig Smash and Warp Surge summons) are not served.
        if (modifier.AbilityId?.StartsWith(HseStatBoostAbilityPrefix, StringComparison.Ordinal) != true)
        {
            return null;
        }

        var constants = abilities.GetValueOrDefault(modifier.AbilityId)?.Constants
            ?? throw HseError(name, $"stat boost '{modifier.AbilityId}' is not in abilities.json");
        int Percent(string key) =>
            constants.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var percent)
                ? percent
                : throw HseError(name, $"stat boost '{modifier.AbilityId}' has no whole '{key}'");

        return new GameCatalogHseModifier(
            "StatBoost",
            null,
            null,
            null,
            Percent("healthPct"),
            Percent("dmgPct"),
            Percent("armorPct"),
            gameModes,
            HseUnitFilter(
                name,
                constants.TryGetValue("trait", out var trait) ? new DatamineHseRestriction([trait], null) : null,
                constants.TryGetValue("factionId", out var faction) ? new DatamineHseRestriction([faction], null) : null,
                null));
    }

    private static string HseGameModeName(string mode) => HseGameModeNames.GetValueOrDefault(mode, mode);

    private static GameCatalogHseRestriction? HseRestriction(string name, DatamineHseRestriction? raw, Func<string, string>? map = null)
    {
        if (raw is null)
        {
            return null;
        }

        if (raw.Allowed is { Count: 0 })
        {
            // An empty allow list would mean "nothing passes"; null means "all pass".
            throw HseError(name, "has an empty allowed list");
        }

        if (raw.Allowed is null && raw.Disallowed is not { Count: > 0 })
        {
            return null;
        }

        map ??= value => value;
        return new GameCatalogHseRestriction(
            raw.Allowed?.Select(map).ToArray(),
            raw.Disallowed is { Count: > 0 } disallowed ? disallowed.Select(map).ToArray() : null);
    }

    private static GameCatalogHseUnitFilter? HseUnitFilter(
        string name, DatamineHseRestriction? tags, DatamineHseRestriction? factions, IReadOnlyList<string>? unitIds)
    {
        var filter = new GameCatalogHseUnitFilter(HseRestriction(name, tags), HseRestriction(name, factions), unitIds);
        return filter is { Tags: null, FactionIds: null, UnitIds: null } ? null : filter;
    }

    private static InvalidOperationException HseError(string eventName, string detail) =>
        new($"Dataset '{GameCatalogDatasets.HomeScreenEventScoring}' HSE '{eventName}' {detail}.");

    [GeneratedRegex(@"^(\w+)%(\d+)/(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex QuestChanceOfRegex();

    /// <summary>
    /// Reads the datamine <c>linearHeroEvent.json</c> into one Quest Event per featured hero, with the enemy faction
    /// from <c>trackingAdditionalData</c>, each battle's upgrade drop from <c>loot.chanceOf</c>, and its enemies from
    /// the <c>"npcId:progressionIndex"</c> strings.
    /// </summary>
    /// <exception cref="InvalidOperationException">A battle cannot be read.</exception>
    public static IReadOnlyList<GameCatalogRawQuestEvent> ReadDatamineQuestEvents(IReadOnlyList<DatamineLinearHeroEvent> events) =>
        events.Select(quest => new GameCatalogRawQuestEvent(
            quest.UnitId,
            quest.AllowedFactions,
            [QuestEnemyFaction(quest)],
            quest.Tiers
                .Select(tier => new GameCatalogRawQuestTier(
                    tier.Index,
                    tier.Battles.Select(battle => ReadQuestBattle(quest.UnitId, battle)).ToArray()))
                .ToArray())).ToArray();

    private static string QuestEnemyFaction(DatamineLinearHeroEvent quest)
    {
        using var data = JsonDocument.Parse(quest.TrackingAdditionalData);
        return data.RootElement.TryGetProperty("enemyFaction", out var faction) && faction.GetString() is { Length: > 0 } id
            ? id
            : throw QuestError(quest.UnitId, "has no enemyFaction in trackingAdditionalData");
    }

    private static GameCatalogRawQuestBattle ReadQuestBattle(
        string unitId, DatamineLinearHeroBattle battle)
    {
        var drop = QuestChanceOfRegex().Match(battle.Loot.ChanceOf ?? string.Empty);
        if (!drop.Success)
        {
            throw QuestError(unitId, $"battle {battle.BattleNr} has no 'upgradeId%num/denom' drop");
        }

        return new GameCatalogRawQuestBattle(
            battle.BattleNr,
            battle.BoardId,
            battle.SpawnPointsSet,
            battle.LightningVictory,
            new GameCatalogRawQuestLoot(new GameCatalogRawQuestLootReward(
                drop.Groups[1].Value,
                int.Parse(drop.Groups[2].ValueSpan, CultureInfo.InvariantCulture),
                int.Parse(drop.Groups[3].ValueSpan, CultureInfo.InvariantCulture))),
            battle.Enemies.Select(enemy =>
            {
                var colon = enemy.IndexOf(':', StringComparison.Ordinal);
                return colon == -1
                    ? new GameCatalogRawQuestEnemy(enemy, null)
                    : new GameCatalogRawQuestEnemy(
                        enemy[..colon],
                        int.TryParse(enemy.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                            ? index
                            : throw QuestError(unitId, $"battle {battle.BattleNr} enemy '{enemy}' has no whole progression index"));
            }).ToArray(),
            battle.Objectives);
    }

    private static InvalidOperationException QuestError(string unitId, string detail) =>
        new($"Dataset '{GameCatalogDatasets.QuestEvents}' quest '{unitId}' {detail}.");
}
