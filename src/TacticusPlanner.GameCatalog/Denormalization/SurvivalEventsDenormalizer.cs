using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine seasonal events (Survival is a part of some of them).</summary>
    public const string DatamineSeasonalEventsPath = "gameconfig/liveEvents/seasonalEvent.json";

    /// <summary>Raw path of the datamine shop offers.</summary>
    public const string DatamineOffersPath = "gameconfig/shop/offers.json";

    /// <summary>Raw path of the datamine real-money products (offer prices and rewards).</summary>
    public const string DatamineRealMoneyProductsPath = "gameconfig/shop/realMoneyProducts.json";

    /// <summary>The event-definitions type whose occurrences date Survival events.</summary>
    public const string SurvivalEventType = "Survival";

    /// <summary>The <c>survival</c> occurrence parameter naming the served survival-events id it dates.</summary>
    public const string SurvivalEventIdParameter = "survivalEventId";

    /// <summary>Characters a Survival team may field besides the featured hero, who always fights.</summary>
    public const int SurvivalMaxCharacters = 10;

    /// <summary>Machines of War a Survival team may field when the event allows them.</summary>
    public const int SurvivalMaxMows = 3;

    private const string SummonPowerUpPrefix = "powupSummonSpawn_";
    private const string CloneWaveScoreKey = "CLONE_WAVE";
    private const string GlobalAbilityModifier = "battleGlobalActiveAbility";

    private static readonly string[] SurvivalMedals = ["iron", "bronze", "silver", "gold", "platinum", "diamond", "adamantine"];

    [GeneratedRegex("^season_(?<prefix>[a-z0-9_]+)_event$")]
    private static partial Regex SeasonalEventNamePattern();

    [GeneratedRegex("^product_(?<prefix>[a-z0-9_]+)_progress_chest_(?<level>[0-9]+)$")]
    private static partial Regex ProgressChestProductPattern();

    /// <summary>The served id of a seasonal event name: <c>season_december_2026_event</c> → <c>december-2026</c>.</summary>
    public static string SurvivalEventId(string eventName) =>
        GameCatalogSurvivalEventIds.FromEventName(eventName)
        ?? throw SurvivalError(eventName, "the event name is not 'season_<id>_event'.");

    private static string SeasonalEventPrefix(string eventName)
    {
        var match = SeasonalEventNamePattern().Match(eventName);
        return match.Success
            ? match.Groups["prefix"].Value
            : throw SurvivalError(eventName, "the event name is not 'season_<id>_event'.");
    }

    /// <summary>
    /// Builds one survival-events record per seasonal event with <c>includesSurvival</c>, ordered by id, per the
    /// survival-events-dataset spec: the event's rules and waves from its <c>survival</c> block and battle set, its
    /// milestones from <c>rewardProgressId</c>, its progress chests from the chest products it pays for in its
    /// currency, its missions from the seasonal quest groups by prefix, and its offers from <c>initialOfferIds</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">An event does not hold together (see the spec's load failures).</exception>
    public static IReadOnlyList<GameCatalogSurvivalEvent> BuildSurvivalEvents(
        DatamineSurvivalSources sources, JsonSerializerOptions options)
    {
        var seasonalQuests = ReadSeasonalQuests(sources.QuestGroups, options);
        var survivalDefinitions = sources.EventDefinitions
            .Where(definition => definition.Type == SurvivalEventType)
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        var occurrencesById = sources.EventOccurrences
            .Where(occurrence => survivalDefinitions.Contains(occurrence.DefinitionId)
                && occurrence.Parameters?.TryGetValue(SurvivalEventIdParameter, out var id) == true
                && id.ValueKind == JsonValueKind.String)
            .GroupBy(occurrence => occurrence.Parameters![SurvivalEventIdParameter].GetString()!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(occurrence => occurrence.StartUtc).First(), StringComparer.Ordinal);
        var offersById = sources.Offers
            .Where(offer => offer.ValueKind == JsonValueKind.Object
                && offer.TryGetProperty("offerId", out var id)
                && id.ValueKind == JsonValueKind.String)
            .GroupBy(offer => offer.GetProperty("offerId").GetString()!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return sources.SeasonalEvents
            .Where(seasonalEvent => seasonalEvent.IncludesSurvival == true)
            .Select(seasonalEvent => BuildSurvivalEvent(
                seasonalEvent,
                sources,
                seasonalQuests,
                offersById,
                occurrencesById.GetValueOrDefault(SurvivalEventId(seasonalEvent.EventName)),
                options))
            .OrderBy(survivalEvent => survivalEvent.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static GameCatalogSurvivalEvent BuildSurvivalEvent(
        DatamineSeasonalEvent seasonalEvent,
        DatamineSurvivalSources sources,
        IReadOnlyDictionary<string, IReadOnlyList<DatamineSeasonalQuest>> seasonalQuests,
        IReadOnlyDictionary<string, JsonElement> offersById,
        GameCatalogEventOccurrence? occurrence,
        JsonSerializerOptions options)
    {
        var eventKey = seasonalEvent.EventName;
        InvalidOperationException Invalid(string message) => SurvivalError(eventKey, message);

        var prefix = SeasonalEventPrefix(eventKey);
        var survival = seasonalEvent.Survival ?? throw Invalid("'survival' is missing.");
        var featured = Required(survival.FeaturedHeroId, "survival.featuredHeroId");
        var currency = Required(seasonalEvent.ResourceId, "resourceId");
        var battle = ReadSurvivalBattle(Required(survival.BattleSetId, "survival.battleSetId"));
        var waves = BuildSurvivalWaves(battle, Invalid);

        var mowsAllowed = survival.IsMachinesOfWarAllowedInBattle == true;
        var disallowed = (battle.DisallowedFactions ?? []).ToHashSet(StringComparer.Ordinal);
        var unknownFaction = disallowed.FirstOrDefault(faction => !sources.FactionIds.Contains(faction, StringComparer.Ordinal));
        if (unknownFaction is not null)
        {
            throw Invalid($"the battle disallows faction '{unknownFaction}', which is not a served faction.");
        }

        var rules = new GameCatalogSurvivalRules(
            [.. sources.FactionIds.Where(faction => !disallowed.Contains(faction))],
            mowsAllowed,
            SurvivalMaxCharacters,
            mowsAllowed ? SurvivalMaxMows : 0,
            new GameCatalogSurvivalStamina(
                RequiredInt(survival.StartStamina, "survival.startStamina"),
                RequiredInt(survival.MaxStamina, "survival.maxStamina"),
                RequiredInt(survival.StaminaRegenerationTime, "survival.staminaRegenerationTime"),
                RequiredInt(survival.StaminaRegenerationAmount, "survival.staminaRegenerationAmount")),
            survival.StatBoostPct ?? 0,
            survival.StatBoostAffectedStats ?? [],
            ReadLastStand(survival, Invalid),
            ReadSummon(survival, Invalid),
            [.. (survival.PersistentRarityUps ?? new Dictionary<string, int>())
                .Select(pair => new GameCatalogSurvivalRarityWave(SurvivalRarity(pair.Key, Invalid), pair.Value))
                .OrderBy(entry => entry.Wave)],
            [.. (survival.FeaturedHeroStatOverrides ?? new Dictionary<string, DatamineSurvivalStatOverride>())
                .Select(pair => new GameCatalogSurvivalStatOverride(
                    SurvivalRarity(pair.Key, Invalid),
                    pair.Value.CritChance,
                    pair.Value.CritDmg,
                    pair.Value.BlockChance,
                    pair.Value.BlockDmg))
                .OrderBy(entry => Array.IndexOf(GuildWarRarities, entry.Rarity))]);

        var points = seasonalEvent.MaxPayouts?.Points ?? throw Invalid("'maxPayouts.points' is missing.");
        var milestonesKey = Required(seasonalEvent.RewardProgressId, "rewardProgressId");
        if (!sources.TieredProgressRewards.ContainsKey(milestonesKey))
        {
            throw Invalid($"rewardProgressId '{milestonesKey}' is not a tieredProgressRewards ladder.");
        }

        return new GameCatalogSurvivalEvent(
            SurvivalEventId(eventKey),
            eventKey,
            occurrence?.DefinitionId,
            occurrence?.StartUtc,
            occurrence?.EndUtc,
            featured,
            currency,
            rules,
            waves,
            BuildSurvivalScoring(eventKey, survival, Invalid),
            new GameCatalogSurvivalPoints(
                RequiredInt(seasonalEvent.MaxPoints, "maxPoints"),
                points.GetValueOrDefault("freeMissions"),
                points.GetValueOrDefault("premiumMissions"),
                points.GetValueOrDefault("survivalFree"),
                points.GetValueOrDefault("survivalPremium")),
            BuildRewardTiers(GameCatalogDatasets.SurvivalEvents, eventKey, milestonesKey, sources.TieredProgressRewards, sources.Chests),
            BuildSurvivalChests(eventKey, prefix, currency, sources, Invalid),
            BuildMissions(GameCatalogDatasets.SurvivalEvents, eventKey, prefix, seasonalQuests),
            [.. (seasonalEvent.InitialOfferIds ?? [])
                .Select(offerId => BuildSurvivalOffer(eventKey, offerId, offersById, sources.RealMoneyProducts, Invalid))]);

        string Required(string? value, string field) => value ?? throw Invalid($"'{field}' is missing.");

        int RequiredInt(int? value, string field) => value ?? throw Invalid($"'{field}' is missing.");

        DatamineSurvivalBattle ReadSurvivalBattle(string battleSetId)
        {
            if (!sources.BattleSets.TryGetValue(battleSetId, out var set) || set.ValueKind != JsonValueKind.Array)
            {
                throw Invalid($"battle set '{battleSetId}' is missing or not a list of battles.");
            }

            var battles = set.Deserialize<List<DatamineSurvivalBattle>>(options) ?? [];
            return battles.Count == 1
                ? battles[0]
                : throw Invalid($"battle set '{battleSetId}' has {battles.Count} battles; expected 1.");
        }
    }

    private static GameCatalogSurvivalWave[] BuildSurvivalWaves(
        DatamineSurvivalBattle battle, Func<string, InvalidOperationException> invalid)
    {
        var waves = battle.Waves ?? [];
        if (waves.Count == 0)
        {
            throw invalid("the battle has no waves.");
        }

        return [.. waves.Select((wave, index) =>
        {
            var clone = wave.IsCloneWave == true;
            if (clone && wave.CloneWaveHealthPct is null)
            {
                throw invalid($"clone wave {index + 1} has no cloneWaveHealthPct.");
            }

            if (!clone && (wave.Army is null || wave.Army.Count == 0))
            {
                throw invalid($"wave {index + 1} has no army.");
            }

            return new GameCatalogSurvivalWave(
                index + 1,
                wave.Round,
                wave.Power,
                wave.ArmyScore,
                clone ? wave.CloneWaveHealthPct : null,
                [.. (wave.Army ?? [])
                    .Select(entry => ParseNpcEntry(entry, invalid))
                    .GroupBy(entry => entry)
                    .Select(group => new GameCatalogSurvivalUnitCount(group.Key.NpcId, group.Key.StatsIndex, group.Count()))],
                wave.RarityUpgradeOnCompletion is { } rarity ? SurvivalRarity(rarity, invalid) : null,
                [.. (wave.ArmyAfterCompletion ?? [])
                    .Select(entry => ParsePowerUp(entry, invalid))
                    .GroupBy(entry => entry)
                    .Select(group => new GameCatalogSurvivalPowerUp(group.Key.Id, group.Key.NpcId, group.Key.StatsIndex, group.Count()))]);
        })];
    }

    private static (string Id, string? NpcId, int? StatsIndex) ParsePowerUp(
        string entry, Func<string, InvalidOperationException> invalid)
    {
        if (!entry.StartsWith(SummonPowerUpPrefix, StringComparison.Ordinal))
        {
            return entry.Contains(':', StringComparison.Ordinal)
                ? throw invalid($"power-up '{entry}' has an index but is not a summon.")
                : (entry, null, null);
        }

        var (npcId, statsIndex) = ParseNpcEntry(entry, invalid);
        return (npcId, npcId, statsIndex);
    }

    /// <summary>An <c>npcId:N</c> entry (1-based stats row).</summary>
    private static (string NpcId, int StatsIndex) ParseNpcEntry(string entry, Func<string, InvalidOperationException> invalid)
    {
        var separator = entry.LastIndexOf(':');
        return separator > 0
            && int.TryParse(entry.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index > 0
                ? (entry[..separator], index)
                : throw invalid($"entry '{entry}' is not \"npcId:N\".");
    }

    private static string SurvivalRarity(string rarity, Func<string, InvalidOperationException> invalid) =>
        GuildWarRarities.Contains(rarity, StringComparer.Ordinal)
            ? rarity
            : throw invalid($"'{rarity}' is not a rarity.");

    private static GameCatalogSurvivalLastStand? ReadLastStand(
        DatamineSurvivalConfig survival, Func<string, InvalidOperationException> invalid)
    {
        if (survival.UseLastStand != true)
        {
            return null;
        }

        var config = survival.LastStandConfig ?? throw invalid("useLastStand is set but 'lastStandConfig' is missing.");
        var buff = config.StrategicBuff ?? throw invalid("'lastStandConfig.strategicBuff' is missing.");
        var separator = buff.IndexOf(':');
        return new GameCatalogSurvivalLastStand(
            separator >= 0 ? buff[(separator + 1)..] : buff,
            config.StrategicMultiplier ?? throw invalid("'lastStandConfig.strategicMultiplier' is missing."));
    }

    private static GameCatalogSurvivalSummon? ReadSummon(
        DatamineSurvivalConfig survival, Func<string, InvalidOperationException> invalid)
    {
        var abilities = (survival.Modifiers ?? [])
            .Where(modifier => modifier.Type == GlobalAbilityModifier)
            .SelectMany(modifier => modifier.Abilities ?? [])
            .ToArray();
        if (abilities.Length == 0)
        {
            return null;
        }

        var unitIds = abilities
            .Select(ability =>
            {
                var id = ability.AbilityId ?? throw invalid("a global ability has no abilityId.");
                var separator = id.IndexOf(':');
                return separator > 0 ? id[(separator + 1)..] : throw invalid($"global ability '{id}' names no unit.");
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unitIds.Length != 1)
        {
            throw invalid($"global abilities summon {unitIds.Length} different units; expected 1.");
        }

        return new GameCatalogSurvivalSummon(
            unitIds[0],
            [.. abilities.Select(ability => new GameCatalogSurvivalSummonLevel(
                SurvivalRarity(ability.RarityCap ?? string.Empty, invalid),
                ability.AbilityLevel ?? throw invalid("a global ability has no abilityLevel.")))]);
    }

    private static GameCatalogSurvivalScoring BuildSurvivalScoring(
        string eventKey, DatamineSurvivalConfig survival, Func<string, InvalidOperationException> invalid)
    {
        var extra = survival.Extra ?? [];
        var medals = SurvivalMedals
            .Where(medal => extra.ContainsKey($"{medal}MedalWave"))
            .Select(medal =>
            {
                var wave = extra[$"{medal}MedalWave"];
                if (wave.ValueKind != JsonValueKind.Number || !extra.TryGetValue($"{medal}MedalReward", out var reward)
                    || reward.ValueKind != JsonValueKind.String)
                {
                    throw invalid($"the {medal} medal needs a numeric wave and a reward string.");
                }

                return new GameCatalogSurvivalMedal(
                    medal,
                    wave.GetInt32(),
                    [ParseRewardRange(GameCatalogDatasets.SurvivalEvents, eventKey, $"{medal}MedalReward", reward.GetString()!)]);
            })
            .ToArray();

        var kills = survival.ScorePerKill ?? new Dictionary<string, int>();
        var cloneScore = kills.TryGetValue(CloneWaveScoreKey, out var clone) ? clone : (int?)null;

        return new GameCatalogSurvivalScoring(
            medals,
            [.. kills
                .Where(pair => pair.Key != CloneWaveScoreKey)
                .Select(pair =>
                {
                    var (npcId, statsIndex) = ParseNpcEntry(pair.Key, invalid);
                    return new GameCatalogSurvivalKillScore(npcId, statsIndex, pair.Value);
                })
                .OrderBy(score => score.NpcId, StringComparer.Ordinal)
                .ThenBy(score => score.StatsIndex)],
            cloneScore,
            survival.ScorePerTurnSkipped ?? 0,
            [.. (survival.RarityCapMilestones ?? new Dictionary<string, string>())
                .Select(pair => new GameCatalogSurvivalRarityMilestone(
                    SurvivalRarity(pair.Key, invalid),
                    [ParseRewardRange(GameCatalogDatasets.SurvivalEvents, eventKey, "rarityCapMilestones", pair.Value)]))
                .OrderBy(entry => Array.IndexOf(GuildWarRarities, entry.Rarity))]);
    }

    /// <summary>
    /// The chest products an event pays for in its currency, named <c>product_&lt;prefix&gt;_progress_chest_NN</c>
    /// where every part of the event's own prefix is a part of the product prefix (so Crescendo A takes
    /// <c>crescendo_event_a</c>, not <c>crescendo_event_b</c>), in level order.
    /// </summary>
    private static GameCatalogSurvivalChest[] BuildSurvivalChests(
        string eventKey,
        string eventPrefix,
        string currency,
        DatamineSurvivalSources sources,
        Func<string, InvalidOperationException> invalid)
    {
        var eventParts = eventPrefix.Split('_');
        return [.. sources.Products
            .Select(pair => (pair.Key, pair.Value, Match: ProgressChestProductPattern().Match(pair.Key)))
            .Where(entry => entry.Match.Success
                && entry.Value.ValueKind == JsonValueKind.Object
                && entry.Value.TryGetProperty("cost", out var cost)
                && cost.ValueKind == JsonValueKind.Object
                && cost.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == currency
                && eventParts.All(part => entry.Match.Groups["prefix"].Value.Split('_').Contains(part, StringComparer.Ordinal)))
            .Select(entry =>
            {
                var cost = entry.Value.GetProperty("cost");
                var chestId = entry.Value.TryGetProperty("chestId", out var id) && id.ValueKind == JsonValueKind.String
                    ? id.GetString()!
                    : throw invalid($"chest product '{entry.Key}' has no chestId.");
                if (!sources.Chests.TryGetValue(chestId, out var chest))
                {
                    throw invalid($"chest product '{entry.Key}' names chest '{chestId}', which is not in chests.json.");
                }

                return new GameCatalogSurvivalChest(
                    int.Parse(entry.Match.Groups["level"].Value, CultureInfo.InvariantCulture),
                    new GameCatalogSurvivalCost(currency, cost.TryGetProperty("amount", out var amount) ? amount.GetInt32() : 0),
                    [.. ChestRewardStrings(chest).Select(reward =>
                        ParseRewardRange(GameCatalogDatasets.SurvivalEvents, eventKey, chestId, reward))]);
            })
            .OrderBy(chest => chest.Level)];
    }

    private static GameCatalogSurvivalOffer BuildSurvivalOffer(
        string eventKey,
        string offerId,
        IReadOnlyDictionary<string, JsonElement> offersById,
        IReadOnlyDictionary<string, JsonElement> realMoneyProducts,
        Func<string, InvalidOperationException> invalid)
    {
        if (!offersById.TryGetValue(offerId, out var offer))
        {
            throw invalid($"initial offer '{offerId}' is not in offers.json.");
        }

        var productId = offer.TryGetProperty("realMoneyProductId", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()!
            : throw invalid($"offer '{offerId}' has no realMoneyProductId.");
        if (!realMoneyProducts.TryGetValue(productId, out var product) || product.ValueKind != JsonValueKind.Object)
        {
            throw invalid($"offer '{offerId}' names product '{productId}', which is not in realMoneyProducts.json.");
        }

        var price = product.TryGetProperty("price", out var priceValue) && priceValue.ValueKind == JsonValueKind.Number
            ? priceValue.GetInt32()
            : throw invalid($"product '{productId}' has no price.");
        var maxPurchases = offer.TryGetProperty("maxPurchases", out var max) && max.ValueKind == JsonValueKind.Number
            ? max.GetInt32()
            : (int?)null;
        var rewards = product.TryGetProperty("rewards", out var rewardList) && rewardList.ValueKind == JsonValueKind.Array
            ? rewardList.EnumerateArray()
                .Select(reward => ParseRewardRange(GameCatalogDatasets.SurvivalEvents, eventKey, productId, reward.GetString() ?? string.Empty))
                .ToArray()
            : [];

        return new GameCatalogSurvivalOffer(offerId, offerId[(offerId.LastIndexOf('_') + 1)..], price, maxPurchases, rewards);
    }

    private static InvalidOperationException SurvivalError(string eventKey, string message) =>
        EventDatasetError(GameCatalogDatasets.SurvivalEvents, eventKey, message);
}
