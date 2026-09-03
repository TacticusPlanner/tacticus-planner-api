using FastEndpoints;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.GameCatalog;

/// <summary>
/// Serves one whole denormalized dataset (no route parameter). The payload is already self-contained
/// (reference tables inlined), so the client consumes it without any cross-dataset joins.
/// </summary>
public abstract class ServedDatasetEndpoint<TPayload>(IGameCatalogProvider catalog, string datasetKey)
    : EndpointWithoutRequest<GameCatalogDatasetEnvelope<TPayload>>
{
    protected GameCatalogSnapshot Snapshot => catalog.Current;

    protected abstract TPayload Payload { get; }

    public override Task HandleAsync(CancellationToken ct)
    {
        var snapshot = Snapshot;
        var response = new GameCatalogDatasetEnvelope<TPayload>(
            snapshot.Version,
            snapshot.SchemaVersion,
            snapshot.GameVersion,
            snapshot.SourceHash,
            datasetKey,
            snapshot.DatasetHashes[datasetKey],
            Payload
        );

        return Send.OkAsync(response, ct);
    }

    protected void ConfigureServed(string summary, string okDescription)
    {
        AllowAnonymous();
        Summary(endpointSummary =>
        {
            endpointSummary.Summary = summary;
            endpointSummary.Response<GameCatalogDatasetEnvelope<TPayload>>(StatusCodes.Status200OK, okDescription);
        });
    }
}

public sealed class GetGameCatalogCharactersEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogCharacterView>>(catalog, GameCatalogDatasets.Characters)
{
    protected override IReadOnlyList<GameCatalogCharacterView> Payload => Snapshot.CharacterViews;

    public override void Configure()
    {
        Get("game-catalog/characters");
        ConfigureServed("Gets game catalog characters.", "All characters with shard farm locations and eligible equipment.");
    }
}

public sealed class GetGameCatalogNpcsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogNpc>>(catalog, GameCatalogDatasets.Npcs)
{
    protected override IReadOnlyList<GameCatalogNpc> Payload => Snapshot.NpcList;

    public override void Configure()
    {
        Get("game-catalog/npcs");
        ConfigureServed("Gets game catalog NPCs.", "All non-playable units.");
    }
}

public sealed class GetGameCatalogMowsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogMow>>(catalog, GameCatalogDatasets.Mows)
{
    protected override IReadOnlyList<GameCatalogMow> Payload => Snapshot.MowList;

    public override void Configure()
    {
        Get("game-catalog/mows");
        ConfigureServed("Gets game catalog machines of war.", "All machines of war.");
    }
}

public sealed class GetGameCatalogMowUpgradeCostsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogMowUpgradeCostView>>(catalog, GameCatalogDatasets.MowUpgradeCostsServed)
{
    protected override IReadOnlyList<GameCatalogMowUpgradeCostView> Payload => Snapshot.MowUpgradeCostViews;

    public override void Configure()
    {
        Get("game-catalog/mow-upgrade-costs");
        ConfigureServed("Gets the machine-of-war upgrade-cost ladder.", "The shared per-level upgrade-cost ladder for all machines of war.");
    }
}

public sealed class GetGameCatalogAscensionCostsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogAscensionCostView>>(catalog, GameCatalogDatasets.AscensionCostsServed)
{
    protected override IReadOnlyList<GameCatalogAscensionCostView> Payload => Snapshot.AscensionCostViews;

    public override void Configure()
    {
        Get("game-catalog/ascension-costs");
        ConfigureServed("Gets the ascension-orb/shard cost ladder.", "The shared per-progression-step ascension cost ladder (shards, mythic shards, orbs) for all characters.");
    }
}

public sealed class GetGameCatalogUnlockShardCostsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogUnlockShardCostView>>(catalog, GameCatalogDatasets.UnlockShardCostsServed)
{
    protected override IReadOnlyList<GameCatalogUnlockShardCostView> Payload => Snapshot.UnlockShardCostViews;

    public override void Configure()
    {
        Get("game-catalog/unlock-shard-costs");
        ConfigureServed("Gets the per-rarity character unlock shard cost table.", "The shard count required to unlock a character of each rarity.");
    }
}

public sealed class GetGameCatalogOnslaughtRewardsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogOnslaughtReward>>(catalog, GameCatalogDatasets.OnslaughtRewards)
{
    protected override IReadOnlyList<GameCatalogOnslaughtReward> Payload => Snapshot.OnslaughtRewards;

    public override void Configure()
    {
        Get("game-catalog/onslaught-rewards");
        ConfigureServed("Gets the Onslaught reward table.", "Reward ranges by sector, tier, and rarity.");
    }
}

public sealed class GetGameCatalogUpgradesEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogUpgradeView>>(catalog, GameCatalogDatasets.Upgrades)
{
    protected override IReadOnlyList<GameCatalogUpgradeView> Payload => Snapshot.UpgradeViews;

    public override void Configure()
    {
        Get("game-catalog/upgrades");
        ConfigureServed("Gets game catalog upgrade materials.", "All upgrades with farm locations and expanded recipes.");
    }
}

public sealed class GetGameCatalogEquipmentEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogEquipmentView>>(catalog, GameCatalogDatasets.Equipment)
{
    protected override IReadOnlyList<GameCatalogEquipmentView> Payload => Snapshot.EquipmentViews;

    public override void Configure()
    {
        Get("game-catalog/equipment");
        ConfigureServed("Gets game catalog equipment.", "All equipment with the matched per-rarity upgrade-cost ladder inlined.");
    }
}

public sealed class GetGameCatalogCampaignBattlesEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogCampaignBattleView>>(catalog, GameCatalogDatasets.CampaignBattles)
{
    protected override IReadOnlyList<GameCatalogCampaignBattleView> Payload => Snapshot.CampaignBattleViews;

    public override void Configure()
    {
        Get("game-catalog/campaign-battles");
        ConfigureServed("Gets game catalog campaign battles.", "All campaign battles (keyed by battle id, each with its campaignGroupId) with inlined reward drop chances.");
    }
}

public sealed class GetGameCatalogCampaignDefinitionsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogCampaignDefinitionView>>(catalog, GameCatalogDatasets.CampaignDefinitions)
{
    protected override IReadOnlyList<GameCatalogCampaignDefinitionView> Payload => Snapshot.CampaignDefinitionViews;

    public override void Configure()
    {
        Get("game-catalog/campaign-definitions");
        ConfigureServed("Gets game catalog campaign definitions.", "All campaign groups (keyed by groupId) with metadata and the ids of their battles.");
    }
}

public sealed class GetGameCatalogLresEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogLreView>>(catalog, GameCatalogDatasets.Lres)
{
    protected override IReadOnlyList<GameCatalogLreView> Payload => Snapshot.LreViews;

    public override void Configure()
    {
        Get("game-catalog/lres");
        ConfigureServed("Gets game catalog legendary release events.", "All legendary release events (keyed by id) with per-track metadata and the ids of their battles.");
    }
}

public sealed class GetGameCatalogLreBattlesEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogLreBattleView>>(catalog, GameCatalogDatasets.LreBattles)
{
    protected override IReadOnlyList<GameCatalogLreBattleView> Payload => Snapshot.LreBattleViews;

    public override void Configure()
    {
        Get("game-catalog/lre-battles");
        ConfigureServed("Gets game catalog LRE battles.", "All LRE battles (keyed \"{lreId}-{track}-{number}\", each tagged with its lreId and track) with their waves.");
    }
}

public sealed class GetGameCatalogLreCommonEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogLreCommon>>(catalog, GameCatalogDatasets.LreCommon)
{
    protected override IReadOnlyList<GameCatalogLreCommon> Payload => Snapshot.LreCommonViews;

    public override void Configure()
    {
        Get("game-catalog/lre-common");
        ConfigureServed("Gets the shared LRE reward ladder.", "The single shared LRE reward ladder (points/chests milestones, progression, shards per chest).");
    }
}

public sealed class GetGameCatalogEventDefinitionsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogEventDefinition>>(catalog, GameCatalogDatasets.EventDefinitionsServed)
{
    protected override IReadOnlyList<GameCatalogEventDefinition> Payload => Snapshot.EventDefinitionViews;

    public override void Configure()
    {
        Get("game-catalog/event-definitions");
        ConfigureServed("Gets game catalog event definitions.", "Reusable event mechanics (scoring, applicable game modes, recurrence, required parameters) — no display text or icon; clients resolve those from each definition's id.");
    }
}

public sealed class GetGameCatalogEventsCalendarEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEventsCalendarEntry>>>(catalog, GameCatalogDatasets.EventsCalendar)
{
    protected override IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEventsCalendarEntry>> Payload => Snapshot.EventsCalendar;

    public override void Configure()
    {
        Get("game-catalog/events-calendar");
        ConfigureServed(
            "Gets the game events calendar.",
            "Date-indexed (ISO date key) event occurrences and projected placeholders; an entry spanning multiple dates is repeated under every date it spans.");
    }
}

public sealed class GetGameCatalogShopsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogShopView>>(catalog, GameCatalogDatasets.Shops)
{
    protected override IReadOnlyList<GameCatalogShopView> Payload => Snapshot.ShopViews;

    public override void Configure()
    {
        Get("game-catalog/shops");
        ConfigureServed(
            "Gets the always-on daily shops.",
            "One record per daily shop (guild, war, rogue-trader, crusade) with its rotating slots, structured rewards/costs, explicit day-of-week availability, and opaque lock ids — no display text or icons.");
    }
}
