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
        ConfigureServed("Gets game catalog legendary release events.", "All legendary release events with per-track available units.");
    }
}
