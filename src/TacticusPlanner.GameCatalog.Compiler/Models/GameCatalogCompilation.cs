namespace TacticusPlanner.GameCatalog.Models;

/// <summary>The full result of compiling the raw catalog: release metadata, the raw source collections the
/// validators and denormalizers read, and every served payload. The API reads the lean
/// <see cref="GameCatalogSnapshot"/> built from it instead.</summary>
public sealed record GameCatalogCompilation(
    string Version,
    string SchemaHash,
    string GameVersion,
    string SourceHash,
    IReadOnlyDictionary<string, string> DatasetHashes,
    // Raw source collections — kept for validation and the existing derived views.
    IReadOnlyDictionary<string, GameCatalogFactionUnits> UnitsByFaction,
    IReadOnlyList<GameCatalogMowUpgradeCost> MowUpgradeCosts,
    IReadOnlyList<GameCatalogCharacterAbilityCost> CharacterAbilityCosts,
    IReadOnlyList<GameCatalogAscensionCost> AscensionCosts,
    IReadOnlyList<GameCatalogUnlockShardCost> UnlockShardCosts,
    IReadOnlyList<GameCatalogOnslaughtReward> OnslaughtRewards,
    IReadOnlyList<GameCatalogEventDefinition> EventDefinitions,
    IReadOnlyList<GameCatalogEventOccurrence> EventOccurrences,
    IReadOnlyList<GameCatalogHseScoring> HseScoring,
    IReadOnlyDictionary<string, GameCatalogFactionNpcs> NpcsByFaction,
    // Equipment from the datamine items.json, keyed by item type (I_Crit, ...).
    IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEquipment>> EquipmentByType,
    IReadOnlyDictionary<string, IReadOnlyList<GameCatalogUpgrade>> UpgradesByRarity,
    IReadOnlyDictionary<string, GameCatalogCampaignGroup> CampaignGroups,
    IReadOnlyList<GameCatalogDropChance> DropChances,
    IReadOnlyDictionary<string, GameCatalogLre> LresByEvent,
    // Raw daily-shop source files, keyed by source dataset key (shops-guild, ...). Kept for validation
    // (reward/free-offer/cost parse checks run over the raw "type:qty" strings).
    IReadOnlyDictionary<string, GameCatalogRawShop> RawShopsBySourceKey,
    // Seasonal event shops as discovered (merchant + calendar week per week). Kept for validation (week, merchant,
    // currency and variant checks) and for event shop plan writes.
    IReadOnlyList<GameCatalogRawEventShop> RawEventShops,
    // Raw raid-boss source (one file, ported from V1's guild_boss.json). Kept for validation — the
    // encounter unit / field-npc / modifier cross-reference checks run over the raw collections.
    GameCatalogRaidBossRawData RaidBossRawData,
    GameCatalogGuildRaidMetaRawData GuildRaidMetaRawData,
    // Raw mythic quests (reward strings unparsed). Kept for validation — the reward parse and character /
    // equipment cross-reference checks run over the raw strings.
    IReadOnlyList<GameCatalogRawMythicQuests> MythicQuests,
    // Raw unit-power file. Kept for validation — the lineup / damage-type / upgrade-stat checks run over
    // the raw maps against the raw unit, npc and upgrade collections.
    GameCatalogRawUnitPower UnitPower,
    // Raw quest events (V1 shape) and crates. Kept for validation — the tier split, references and the 8,800-point
    // check run over the raw battles.
    IReadOnlyList<GameCatalogRawQuestEvent> QuestEvents,
    IReadOnlyList<GameCatalogQuestCrate> QuestCrates,
    // Served (denormalized) payloads — the public catalog surface.
    IReadOnlyList<GameCatalogCharacterView> CharacterViews,
    IReadOnlyList<GameCatalogNpc> NpcList,
    IReadOnlyList<GameCatalogMow> MowList,
    IReadOnlyList<GameCatalogMowUpgradeCostView> MowUpgradeCostViews,
    IReadOnlyList<GameCatalogCharacterAbilityCostView> CharacterAbilityCostViews,
    IReadOnlyList<GameCatalogAscensionCostView> AscensionCostViews,
    IReadOnlyList<GameCatalogUnlockShardCostView> UnlockShardCostViews,
    IReadOnlyList<GameCatalogUpgradeView> UpgradeViews,
    IReadOnlyList<GameCatalogEquipmentView> EquipmentViews,
    IReadOnlyList<GameCatalogCampaignBattleView> CampaignBattleViews,
    IReadOnlyList<GameCatalogCampaignDefinitionView> CampaignDefinitionViews,
    IReadOnlyList<GameCatalogLreView> LreViews,
    IReadOnlyList<GameCatalogLreBattleView> LreBattleViews,
    IReadOnlyList<GameCatalogHordeModeTiers> HordeModeTiersViews,
    IReadOnlyList<GameCatalogNpcTiers> NpcTiersViews,
    IReadOnlyList<GameCatalogEventDefinition> EventDefinitionViews,
    IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEventsCalendarEntry>> EventsCalendar,
    IReadOnlyList<GameCatalogShopView> ShopViews,
    IReadOnlyList<GameCatalogEventShopView> EventShopViews,
    GameCatalogRaidBossesView RaidBossesView,
    GameCatalogGuildRaidMetaView GuildRaidMetaView,
    IReadOnlyList<GameCatalogMythicQuestsView> MythicQuestViews,
    IReadOnlyList<GameCatalogUnitPower> UnitPowerViews,
    IReadOnlyList<GameCatalogQuestEventView> QuestEventViews,
    IReadOnlyList<GameCatalogQuestCrate> QuestCrateViews,
    IReadOnlyList<GameCatalogFaction> FactionViews,
    IReadOnlyList<GameCatalogDamageType> DamageTypeViews,
    IReadOnlyList<GameCatalogGlobalValues> GlobalValuesViews,
    IReadOnlyList<GameCatalogProgressionRules> ProgressionRulesViews,
    IReadOnlyList<GameCatalogHre> HreViews,
    IReadOnlyList<GameCatalogIncursion> IncursionViews,
    IReadOnlyList<GameCatalogNpcEncounter> NpcEncounterViews,
    IReadOnlyList<GameCatalogGuildWarSeason> GuildWarViews,
    IReadOnlyList<GameCatalogSurvivalEvent> SurvivalEventViews
)
{
    /// <summary>The served payloads of this compilation, except the day-dependent calendar.</summary>
    public GameCatalogServedViews ToServedViews() => new(
            CharacterViews,
            NpcList,
            MowList,
            MowUpgradeCostViews,
            CharacterAbilityCostViews,
            AscensionCostViews,
            UnlockShardCostViews,
            OnslaughtRewards,
            UpgradeViews,
            EquipmentViews,
            CampaignBattleViews,
            CampaignDefinitionViews,
            LreViews,
            LreBattleViews,
            HordeModeTiersViews,
            NpcTiersViews,
            EventDefinitionViews,
            ShopViews,
            EventShopViews,
            RaidBossesView,
            GuildRaidMetaView,
            MythicQuestViews,
            UnitPowerViews,
            QuestEventViews,
            QuestCrateViews,
            FactionViews,
            DamageTypeViews,
            GlobalValuesViews,
            ProgressionRulesViews,
            HreViews,
            IncursionViews,
            NpcEncounterViews,
            GuildWarViews,
            SurvivalEventViews);

    /// <summary>The lean runtime snapshot of this compilation: its release metadata, served payloads, calendar and
    /// runtime data. The runtime builds the same snapshot from the compiled catalog.</summary>
    public GameCatalogSnapshot ToSnapshot() => new(
        Version,
        SchemaHash,
        GameVersion,
        SourceHash,
        DatasetHashes,
        ToServedViews(),
        EventsCalendar,
        ToRuntimeData());

    /// <summary>The calendar inputs and the few raw facts the API reads at runtime that no served view carries.</summary>
    public GameCatalogRuntimeData ToRuntimeData() => new(
        EventDefinitions,
        EventOccurrences,
        Lres
            .GroupBy(lre => lre.Id)
            .ToDictionary(group => group.Key, group => group.First().UnitSnowprintId),
        RaidBossRawData.Seasons.Values
            .GroupBy(season => season.GuildBossSeasonConfigId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Where(season => season.LoopFromTier is not null && season.LoopFromSet is not null)
            .ToDictionary(
                season => season.GuildBossSeasonConfigId,
                season => new GameCatalogRaidBossSeasonLoop(season.LoopFromTier!.Value, season.LoopFromSet!.Value),
                StringComparer.Ordinal));

    // The manifest served to clients: release metadata + per-dataset hash and download url. Built from the
    // constructor parameters only (a field initializer may not reference another computed property).
    public GameCatalogManifest Manifest { get; } = new(
        Version,
        SchemaHash,
        GameVersion,
        SourceHash,
        DatasetHashes
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new GameCatalogManifestDataset(
                pair.Key, pair.Value, $"{GameCatalogDatasets.RoutePrefix}/{pair.Key}"))
            .ToArray());

    // Flat views over the split datasets, so derived lookups and validation operate on the whole
    // catalog regardless of how the data is chunked across files. Each references constructor
    // parameters (never another computed property — that is illegal in a field initializer).
    public IReadOnlyList<GameCatalogCharacter> Characters { get; } =
        UnitsByFaction.Values.SelectMany(faction => faction.Characters).ToArray();

    public IReadOnlyList<GameCatalogMow> Mows { get; } =
        UnitsByFaction.Values.SelectMany(faction => faction.Mows).ToArray();

    public IReadOnlyList<GameCatalogRawNpc> Npcs { get; } =
        NpcsByFaction.Values.SelectMany(faction => faction.Npcs).ToArray();

    public IReadOnlyList<GameCatalogEquipment> Equipment { get; } =
        EquipmentByType.Values.SelectMany(items => items).ToArray();

    public IReadOnlyList<GameCatalogUpgrade> Upgrades { get; } =
        UpgradesByRarity.Values.SelectMany(items => items).ToArray();

    public IReadOnlyList<GameCatalogCampaignBattle> CampaignBattles { get; } =
        CampaignGroups.Values.SelectMany(group => group.Battles).ToArray();

    public IReadOnlyList<GameCatalogLre> Lres { get; } = LresByEvent.Values.ToArray();
}
