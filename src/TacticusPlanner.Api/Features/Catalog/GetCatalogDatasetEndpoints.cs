using FastEndpoints;
using TacticusPlanner.Catalog;

namespace TacticusPlanner.Api.Features.Catalog;

/// <summary>
/// Serves one whole denormalized dataset (no route parameter). The payload is already self-contained
/// (reference tables inlined), so the client consumes it without any cross-dataset joins.
/// </summary>
public abstract class ServedDatasetEndpoint<TPayload>(ICatalogProvider catalog, string datasetKey)
    : EndpointWithoutRequest<CatalogDatasetEnvelope<TPayload>>
{
    protected CatalogSnapshot Snapshot => catalog.Current;

    protected abstract TPayload Payload { get; }

    public override Task HandleAsync(CancellationToken ct)
    {
        var snapshot = Snapshot;
        var response = new CatalogDatasetEnvelope<TPayload>(
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
            endpointSummary.Response<CatalogDatasetEnvelope<TPayload>>(StatusCodes.Status200OK, okDescription);
        });
    }
}

public sealed class GetCatalogCharactersEndpoint(ICatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<CatalogCharacterView>>(catalog, CatalogDatasets.Characters)
{
    protected override IReadOnlyList<CatalogCharacterView> Payload => Snapshot.CharacterViews;

    public override void Configure()
    {
        Get("catalog/characters");
        ConfigureServed("Gets catalog characters.", "All characters with shard farm locations and eligible equipment.");
    }
}

public sealed class GetCatalogNpcsEndpoint(ICatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<CatalogNpc>>(catalog, CatalogDatasets.Npcs)
{
    protected override IReadOnlyList<CatalogNpc> Payload => Snapshot.NpcList;

    public override void Configure()
    {
        Get("catalog/npcs");
        ConfigureServed("Gets catalog NPCs.", "All non-playable units.");
    }
}

public sealed class GetCatalogMowsEndpoint(ICatalogProvider catalog)
    : ServedDatasetEndpoint<CatalogMowDataset>(catalog, CatalogDatasets.Mows)
{
    protected override CatalogMowDataset Payload => Snapshot.MowDataset;

    public override void Configure()
    {
        Get("catalog/mows");
        ConfigureServed("Gets catalog machines of war.", "All machines of war with the shared upgrade-cost ladder.");
    }
}

public sealed class GetCatalogUpgradesEndpoint(ICatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<CatalogUpgradeView>>(catalog, CatalogDatasets.Upgrades)
{
    protected override IReadOnlyList<CatalogUpgradeView> Payload => Snapshot.UpgradeViews;

    public override void Configure()
    {
        Get("catalog/upgrades");
        ConfigureServed("Gets catalog upgrade materials.", "All upgrades with farm locations and expanded recipes.");
    }
}

public sealed class GetCatalogEquipmentEndpoint(ICatalogProvider catalog)
    : ServedDatasetEndpoint<CatalogEquipmentDataset>(catalog, CatalogDatasets.Equipment)
{
    protected override CatalogEquipmentDataset Payload => Snapshot.EquipmentDataset;

    public override void Configure()
    {
        Get("catalog/equipment");
        ConfigureServed("Gets catalog equipment.", "All equipment with the shared per-rarity upgrade-cost ladders.");
    }
}

public sealed class GetCatalogCampaignBattlesEndpoint(ICatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<CatalogCampaignGroupView>>(catalog, CatalogDatasets.CampaignBattles)
{
    protected override IReadOnlyList<CatalogCampaignGroupView> Payload => Snapshot.CampaignGroupViews;

    public override void Configure()
    {
        Get("catalog/campaign-battles");
        ConfigureServed("Gets catalog campaign battles.", "All campaign groups and battles with inlined reward drop chances.");
    }
}

public sealed class GetCatalogLresEndpoint(ICatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<CatalogLreView>>(catalog, CatalogDatasets.Lres)
{
    protected override IReadOnlyList<CatalogLreView> Payload => Snapshot.LreViews;

    public override void Configure()
    {
        Get("catalog/lres");
        ConfigureServed("Gets catalog legendary release events.", "All legendary release events with per-track available units.");
    }
}
