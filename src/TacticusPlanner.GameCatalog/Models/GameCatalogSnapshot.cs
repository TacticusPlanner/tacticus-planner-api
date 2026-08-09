namespace TacticusPlanner.GameCatalog.Models;

public sealed record GameCatalogSnapshot(
    string Version,
    int SchemaVersion,
    string GameVersion,
    string SourceHash,
    IReadOnlyDictionary<string, string> DatasetHashes,
    // Raw source collections — kept for validation and the existing derived views.
    IReadOnlyDictionary<string, GameCatalogFactionUnits> UnitsByFaction,
    IReadOnlyList<GameCatalogMowUpgradeCost> MowUpgradeCosts,
    IReadOnlyList<GameCatalogEquipmentUpgradeCost> EquipmentUpgradeCosts,
    IReadOnlyList<GameCatalogAscensionCost> AscensionCosts,
    IReadOnlyList<GameCatalogUnlockShardCost> UnlockShardCosts,
    IReadOnlyList<GameCatalogOnslaughtReward> OnslaughtRewards,
    IReadOnlyList<GameCatalogEventDefinition> EventDefinitions,
    IReadOnlyList<GameCatalogEventOccurrence> EventOccurrences,
    IReadOnlyDictionary<string, GameCatalogFactionNpcs> NpcsByFaction,
    IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEquipment>> EquipmentByType,
    IReadOnlyDictionary<string, IReadOnlyList<GameCatalogUpgrade>> UpgradesByRarity,
    IReadOnlyDictionary<string, GameCatalogCampaignGroup> CampaignGroups,
    IReadOnlyList<GameCatalogDropChance> DropChances,
    IReadOnlyDictionary<string, GameCatalogLre> LresByEvent,
    // Served (denormalized) payloads — the public catalog surface.
    IReadOnlyList<GameCatalogCharacterView> CharacterViews,
    IReadOnlyList<GameCatalogNpc> NpcList,
    IReadOnlyList<GameCatalogMow> MowList,
    IReadOnlyList<GameCatalogMowUpgradeCostView> MowUpgradeCostViews,
    IReadOnlyList<GameCatalogAscensionCostView> AscensionCostViews,
    IReadOnlyList<GameCatalogUnlockShardCostView> UnlockShardCostViews,
    IReadOnlyList<GameCatalogUpgradeView> UpgradeViews,
    IReadOnlyList<GameCatalogEquipmentView> EquipmentViews,
    IReadOnlyList<GameCatalogCampaignBattleView> CampaignBattleViews,
    IReadOnlyList<GameCatalogCampaignDefinitionView> CampaignDefinitionViews,
    IReadOnlyList<GameCatalogLreView> LreViews,
    IReadOnlyList<GameCatalogLreBattleView> LreBattleViews,
    IReadOnlyList<GameCatalogLreCommon> LreCommonViews,
    IReadOnlyList<GameCatalogEventDefinition> EventDefinitionViews,
    IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEventsCalendarEntry>> EventsCalendar
)
{
    // The manifest served to clients: release metadata + per-dataset hash and download url. Built from the
    // constructor parameters only (a field initializer may not reference another computed property).
    public GameCatalogManifest Manifest { get; } = new(
        Version,
        SchemaVersion,
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

    public IReadOnlyList<GameCatalogNpc> Npcs { get; } =
        NpcsByFaction.Values.SelectMany(faction => faction.Npcs).ToArray();

    public IReadOnlyList<GameCatalogEquipment> Equipment { get; } =
        EquipmentByType.Values.SelectMany(items => items).ToArray();

    public IReadOnlyList<GameCatalogUpgrade> Upgrades { get; } =
        UpgradesByRarity.Values.SelectMany(items => items).ToArray();

    public IReadOnlyList<GameCatalogCampaignBattle> CampaignBattles { get; } =
        CampaignGroups.Values.SelectMany(group => group.Battles).ToArray();

    public IReadOnlyList<GameCatalogLre> Lres { get; } = LresByEvent.Values.ToArray();
}
