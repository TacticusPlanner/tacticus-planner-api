using FastEndpoints;
using TacticusPlanner.Catalog;

namespace TacticusPlanner.Api.Features.Catalog;

public sealed class GetCatalogUnitsEndpoint(ICatalogProvider catalog)
    : CatalogDatasetEndpoint<CatalogUnit>(catalog, CatalogDatasets.Units)
{
    protected override IReadOnlyList<CatalogUnit> Items => Snapshot.Units;

    public override void Configure()
    {
        Get("catalog/units");
        ConfigureSummary("Gets catalog units.", "The active catalog units.");
    }
}

public sealed class GetCatalogMowsEndpoint(ICatalogProvider catalog)
    : CatalogDatasetEndpoint<CatalogMow>(catalog, CatalogDatasets.Mows)
{
    protected override IReadOnlyList<CatalogMow> Items => Snapshot.Mows;

    public override void Configure()
    {
        Get("catalog/mows");
        ConfigureSummary("Gets catalog machines of war.", "The active catalog machines of war.");
    }
}

public sealed class GetCatalogUpgradesEndpoint(ICatalogProvider catalog)
    : CatalogDatasetEndpoint<CatalogUpgrade>(catalog, CatalogDatasets.Upgrades)
{
    protected override IReadOnlyList<CatalogUpgrade> Items => Snapshot.Upgrades;

    public override void Configure()
    {
        Get("catalog/upgrades");
        ConfigureSummary("Gets catalog upgrade materials.", "The active catalog upgrade materials.");
    }
}

public sealed class GetCatalogEquipmentEndpoint(ICatalogProvider catalog)
    : CatalogDatasetEndpoint<CatalogEquipment>(catalog, CatalogDatasets.Equipment)
{
    protected override IReadOnlyList<CatalogEquipment> Items => Snapshot.Equipment;

    public override void Configure()
    {
        Get("catalog/equipment");
        ConfigureSummary("Gets catalog equipment.", "The active catalog equipment.");
    }
}

public sealed class GetCatalogCampaignsEndpoint(ICatalogProvider catalog)
    : CatalogDatasetEndpoint<CatalogCampaign>(catalog, CatalogDatasets.Campaigns)
{
    protected override IReadOnlyList<CatalogCampaign> Items => Snapshot.Campaigns;

    public override void Configure()
    {
        Get("catalog/campaigns");
        ConfigureSummary("Gets catalog campaigns.", "The active catalog campaigns.");
    }
}

public sealed class GetCatalogCampaignEventsEndpoint(ICatalogProvider catalog)
    : CatalogDatasetEndpoint<CatalogCampaign>(catalog, CatalogDatasets.CampaignEvents)
{
    protected override IReadOnlyList<CatalogCampaign> Items => Snapshot.CampaignEvents;

    public override void Configure()
    {
        Get("catalog/campaign-events");
        ConfigureSummary("Gets catalog campaign events.", "The active catalog campaign events.");
    }
}

public sealed class GetCatalogCampaignBattlesEndpoint(ICatalogProvider catalog)
    : CatalogDatasetEndpoint<CatalogCampaignBattle>(catalog, CatalogDatasets.CampaignBattles)
{
    protected override IReadOnlyList<CatalogCampaignBattle> Items => Snapshot.CampaignBattles;

    public override void Configure()
    {
        Get("catalog/campaign-battles");
        ConfigureSummary("Gets catalog campaign battles.", "The active catalog campaign battles.");
    }
}

public sealed class GetCatalogLresEndpoint(ICatalogProvider catalog)
    : CatalogDatasetEndpoint<CatalogLre>(catalog, CatalogDatasets.Lres)
{
    protected override IReadOnlyList<CatalogLre> Items => Snapshot.Lres;

    public override void Configure()
    {
        Get("catalog/lres");
        ConfigureSummary("Gets catalog legendary release events.", "The active catalog LREs.");
    }
}

public abstract class CatalogDatasetEndpoint<TItem>(ICatalogProvider catalog, string datasetKey)
    : EndpointWithoutRequest<CatalogItemsResponse<TItem>>
{
    protected CatalogSnapshot Snapshot => catalog.Current;

    protected abstract IReadOnlyList<TItem> Items { get; }

    public override Task HandleAsync(CancellationToken ct)
    {
        var snapshot = Snapshot;
        var response = new CatalogItemsResponse<TItem>(
            snapshot.Version,
            snapshot.SchemaVersion,
            snapshot.SourceHash,
            datasetKey,
            snapshot.DatasetHashes[datasetKey],
            Items
        );

        return Send.OkAsync(response, ct);
    }

    protected void ConfigureSummary(string summary, string okDescription)
    {
        Policies(AuthorizationPolicies.AccessAsUser);
        Summary(endpointSummary =>
        {
            endpointSummary.Summary = summary;
            endpointSummary.Response<CatalogItemsResponse<TItem>>(StatusCodes.Status200OK, okDescription);
            endpointSummary.Response(StatusCodes.Status401Unauthorized, "Authentication is required.");
            endpointSummary.Response(StatusCodes.Status403Forbidden, "The authenticated user is not authorized.");
        });
    }
}
