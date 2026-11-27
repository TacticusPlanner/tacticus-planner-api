using System.Runtime.CompilerServices;

namespace TacticusPlanner.GameCatalog.Models;

/// <summary>
/// The catalog the API reads at runtime: release metadata, every served dataset (<paramref name="Views"/> plus the
/// day's <paramref name="EventsCalendar"/>) and <paramref name="Runtime"/>, the calendar inputs and the few raw facts
/// no served view carries. It holds no other raw source collection: those exist only in the compile-time
/// <see cref="GameCatalogCompilation"/>.
/// </summary>
public sealed record GameCatalogSnapshot(
    string Version,
    string SchemaHash,
    string GameVersion,
    string SourceHash,
    IReadOnlyDictionary<string, string> DatasetHashes,
    GameCatalogServedViews Views,
    IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEventsCalendarEntry>> EventsCalendar,
    GameCatalogRuntimeData Runtime
)
{
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

    private static readonly ConditionalWeakTable<GameCatalogServedViews, IReadOnlyDictionary<string, IReadOnlyList<GameCatalogCampaignBattleView>>>
        CampaignBattleIndexes = new();

    /// <summary>Each campaign group's battles in the group's own battle order (campaign-definitions
    /// <c>battleIds</c>), keyed by group id. Built once per <see cref="Views"/>, so a copy with other views gets
    /// its own index.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<GameCatalogCampaignBattleView>> CampaignBattlesByGroup =>
        CampaignBattleIndexes.GetValue(Views, IndexCampaignBattles);

    public IReadOnlyList<GameCatalogEventDefinition> EventDefinitions => Runtime.EventDefinitions;

    public IReadOnlyList<GameCatalogEventOccurrence> EventOccurrences => Runtime.EventOccurrences;

    public IReadOnlyList<GameCatalogCharacterView> CharacterViews => Views.CharacterViews;

    public IReadOnlyList<GameCatalogNpc> NpcList => Views.NpcList;

    public IReadOnlyList<GameCatalogMow> MowList => Views.MowList;

    public IReadOnlyList<GameCatalogMowUpgradeCostView> MowUpgradeCostViews => Views.MowUpgradeCostViews;

    public IReadOnlyList<GameCatalogCharacterAbilityCostView> CharacterAbilityCostViews => Views.CharacterAbilityCostViews;

    public IReadOnlyList<GameCatalogAscensionCostView> AscensionCostViews => Views.AscensionCostViews;

    public IReadOnlyList<GameCatalogUnlockShardCostView> UnlockShardCostViews => Views.UnlockShardCostViews;

    public IReadOnlyList<GameCatalogOnslaughtReward> OnslaughtRewards => Views.OnslaughtRewards;

    public IReadOnlyList<GameCatalogUpgradeView> UpgradeViews => Views.UpgradeViews;

    public IReadOnlyList<GameCatalogEquipmentView> EquipmentViews => Views.EquipmentViews;

    public IReadOnlyList<GameCatalogCampaignBattleView> CampaignBattleViews => Views.CampaignBattleViews;

    public IReadOnlyList<GameCatalogCampaignDefinitionView> CampaignDefinitionViews => Views.CampaignDefinitionViews;

    public IReadOnlyList<GameCatalogLreView> LreViews => Views.LreViews;

    public IReadOnlyList<GameCatalogLreBattleView> LreBattleViews => Views.LreBattleViews;

    public IReadOnlyList<GameCatalogHordeModeTiers> HordeModeTiersViews => Views.HordeModeTiersViews;

    public IReadOnlyList<GameCatalogNpcTiers> NpcTiersViews => Views.NpcTiersViews;

    public IReadOnlyList<GameCatalogEventDefinition> EventDefinitionViews => Views.EventDefinitionViews;

    public IReadOnlyList<GameCatalogShopView> ShopViews => Views.ShopViews;

    public IReadOnlyList<GameCatalogEventShopView> EventShopViews => Views.EventShopViews;

    public GameCatalogRaidBossesView RaidBossesView => Views.RaidBossesView;

    public GameCatalogGuildRaidMetaView GuildRaidMetaView => Views.GuildRaidMetaView;

    public IReadOnlyList<GameCatalogMythicQuestsView> MythicQuestViews => Views.MythicQuestViews;

    public IReadOnlyList<GameCatalogUnitPower> UnitPowerViews => Views.UnitPowerViews;

    public IReadOnlyList<GameCatalogQuestEventView> QuestEventViews => Views.QuestEventViews;

    public IReadOnlyList<GameCatalogQuestCrate> QuestCrateViews => Views.QuestCrateViews;

    public IReadOnlyList<GameCatalogFaction> FactionViews => Views.FactionViews;

    public IReadOnlyList<GameCatalogDamageType> DamageTypeViews => Views.DamageTypeViews;

    public IReadOnlyList<GameCatalogGlobalValues> GlobalValuesViews => Views.GlobalValuesViews;

    public IReadOnlyList<GameCatalogProgressionRules> ProgressionRulesViews => Views.ProgressionRulesViews;

    public IReadOnlyList<GameCatalogHre> HreViews => Views.HreViews;

    public IReadOnlyList<GameCatalogIncursion> IncursionViews => Views.IncursionViews;

    public IReadOnlyList<GameCatalogNpcEncounter> NpcEncounterViews => Views.NpcEncounterViews;

    public IReadOnlyList<GameCatalogGuildWarSeason> GuildWarViews => Views.GuildWarViews;

    public IReadOnlyList<GameCatalogSurvivalEvent> SurvivalEventViews => Views.SurvivalEventViews;

    private static IReadOnlyDictionary<string, IReadOnlyList<GameCatalogCampaignBattleView>> IndexCampaignBattles(GameCatalogServedViews views)
    {
        var battles = views.CampaignBattleViews.ToDictionary(battle => battle.Id, StringComparer.Ordinal);
        return views.CampaignDefinitionViews.ToDictionary(
            definition => definition.GroupId,
            IReadOnlyList<GameCatalogCampaignBattleView> (definition) => [.. definition.BattleIds.Select(id => battles[id])],
            StringComparer.Ordinal);
    }
}
