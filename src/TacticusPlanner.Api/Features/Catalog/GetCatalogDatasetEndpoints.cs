using FastEndpoints;
using TacticusPlanner.Catalog;

namespace TacticusPlanner.Api.Features.Catalog;

public sealed class GetCatalogUnitsEndpoint(ICatalogProvider catalog)
    : EndpointWithoutRequest<CatalogItemsResponse<CatalogUnit>>
{
    public override void Configure()
    {
        Get("catalog/units");
        Policies(AuthorizationPolicies.AccessAsUser);
        Summary(summary =>
        {
            summary.Summary = "Gets catalog units.";
            summary.Response<CatalogItemsResponse<CatalogUnit>>(StatusCodes.Status200OK, "The active catalog units.");
            summary.Response(StatusCodes.Status304NotModified, "The filtered unit result has not changed.");
            summary.Response(StatusCodes.Status401Unauthorized, "Authentication is required.");
            summary.Response(StatusCodes.Status403Forbidden, "The authenticated user is not authorized.");
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var query = CatalogQuery.From(HttpContext);
        var items = catalog.Current.Units.AsEnumerable();

        items = query.ApplySearch(items, unit => [unit.Id, unit.Name, unit.Faction, unit.Alliance]);
        items = query.ApplyEquals(items, "unitKind", unit => unit.UnitKind);
        items = query.ApplyEquals(items, "faction", unit => unit.Faction);
        items = query.ApplyEquals(items, "alliance", unit => unit.Alliance);

        return SendCatalogItemsAsync(CatalogDatasets.Units, items.OrderBy(unit => unit.Name, StringComparer.OrdinalIgnoreCase), query, ct);
    }

    private Task<FastEndpoints.Void> SendCatalogItemsAsync(
        string dataset,
        IEnumerable<CatalogUnit> items,
        CatalogQuery query,
        CancellationToken ct
    )
    {
        var response = CatalogEndpointSender.CreateResponse(HttpContext, catalog.Current, dataset, items, query);

        return response is null
            ? Send.NotModifiedAsync(ct)
            : Send.OkAsync(response, ct);
    }
}

public sealed class GetCatalogMowsEndpoint(ICatalogProvider catalog)
    : EndpointWithoutRequest<CatalogItemsResponse<CatalogMow>>
{
    public override void Configure()
    {
        Get("catalog/mows");
        Policies(AuthorizationPolicies.AccessAsUser);
        Summary(summary =>
        {
            summary.Summary = "Gets catalog machines of war.";
            summary.Response<CatalogItemsResponse<CatalogMow>>(StatusCodes.Status200OK, "The active catalog machines of war.");
            summary.Response(StatusCodes.Status304NotModified, "The filtered MoW result has not changed.");
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var query = CatalogQuery.From(HttpContext);
        var items = catalog.Current.Mows.AsEnumerable();

        items = query.ApplySearch(items, mow => [mow.Id, mow.Name, mow.Faction, mow.Alliance]);
        items = query.ApplyEquals(items, "faction", mow => mow.Faction);
        items = query.ApplyEquals(items, "alliance", mow => mow.Alliance);

        var response = CatalogEndpointSender.CreateResponse(
            HttpContext,
            catalog.Current,
            CatalogDatasets.Mows,
            items.OrderBy(mow => mow.Name, StringComparer.OrdinalIgnoreCase),
            query
        );

        return response is null ? Send.NotModifiedAsync(ct) : Send.OkAsync(response, ct);
    }
}

public sealed class GetCatalogUpgradesEndpoint(ICatalogProvider catalog)
    : EndpointWithoutRequest<CatalogItemsResponse<CatalogUpgrade>>
{
    public override void Configure()
    {
        Get("catalog/upgrades");
        Policies(AuthorizationPolicies.AccessAsUser);
        Summary(summary =>
        {
            summary.Summary = "Gets catalog upgrade materials.";
            summary.Response<CatalogItemsResponse<CatalogUpgrade>>(StatusCodes.Status200OK, "The active catalog upgrade materials.");
            summary.Response(StatusCodes.Status304NotModified, "The filtered upgrade result has not changed.");
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var query = CatalogQuery.From(HttpContext);
        var items = catalog.Current.Upgrades.AsEnumerable();

        items = query.ApplySearch(items, upgrade => [upgrade.Id, upgrade.Material, upgrade.Label, upgrade.Stat]);
        items = query.ApplyEquals(items, "rarity", upgrade => upgrade.Rarity);

        var response = CatalogEndpointSender.CreateResponse(
            HttpContext,
            catalog.Current,
            CatalogDatasets.Upgrades,
            items.OrderBy(upgrade => upgrade.Label, StringComparer.OrdinalIgnoreCase),
            query
        );

        return response is null ? Send.NotModifiedAsync(ct) : Send.OkAsync(response, ct);
    }
}

public sealed class GetCatalogEquipmentEndpoint(ICatalogProvider catalog)
    : EndpointWithoutRequest<CatalogItemsResponse<CatalogEquipment>>
{
    public override void Configure()
    {
        Get("catalog/equipment");
        Policies(AuthorizationPolicies.AccessAsUser);
        Summary(summary =>
        {
            summary.Summary = "Gets catalog equipment.";
            summary.Response<CatalogItemsResponse<CatalogEquipment>>(StatusCodes.Status200OK, "The active catalog equipment.");
            summary.Response(StatusCodes.Status304NotModified, "The filtered equipment result has not changed.");
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var query = CatalogQuery.From(HttpContext);
        var items = catalog.Current.Equipment.AsEnumerable();

        items = query.ApplySearch(items, equipment => [equipment.Id, equipment.Name, equipment.Type]);
        items = query.ApplyEquals(items, "rarity", equipment => equipment.Rarity);
        items = query.ApplyEquals(items, "type", equipment => equipment.Type);

        var response = CatalogEndpointSender.CreateResponse(
            HttpContext,
            catalog.Current,
            CatalogDatasets.Equipment,
            items.OrderBy(equipment => equipment.Name, StringComparer.OrdinalIgnoreCase),
            query
        );

        return response is null ? Send.NotModifiedAsync(ct) : Send.OkAsync(response, ct);
    }
}

public sealed class GetCatalogCampaignsEndpoint(ICatalogProvider catalog)
    : EndpointWithoutRequest<CatalogItemsResponse<CatalogCampaign>>
{
    public override void Configure()
    {
        Get("catalog/campaigns");
        Policies(AuthorizationPolicies.AccessAsUser);
        Summary(summary =>
        {
            summary.Summary = "Gets catalog campaigns.";
            summary.Response<CatalogItemsResponse<CatalogCampaign>>(StatusCodes.Status200OK, "The active catalog campaigns.");
            summary.Response(StatusCodes.Status304NotModified, "The filtered campaign result has not changed.");
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var query = CatalogQuery.From(HttpContext);
        var items = catalog.Current.Campaigns.AsEnumerable();

        items = query.ApplySearch(items, campaign => [campaign.Id, campaign.Name, campaign.DisplayName, campaign.Faction]);
        items = query.ApplyEquals(items, "releaseType", campaign => campaign.ReleaseType);
        items = query.ApplyEquals(items, "groupType", campaign => campaign.GroupType);
        items = query.ApplyEquals(items, "difficulty", campaign => campaign.Difficulty);

        var response = CatalogEndpointSender.CreateResponse(
            HttpContext,
            catalog.Current,
            CatalogDatasets.Campaigns,
            items,
            query,
            [new KeyValuePair<string, string?>("view", "campaign-events")]
        );

        return response is null ? Send.NotModifiedAsync(ct) : Send.OkAsync(response, ct);
    }
}

public sealed class GetCatalogCampaignEventsEndpoint(ICatalogProvider catalog)
    : EndpointWithoutRequest<CatalogItemsResponse<CatalogCampaign>>
{
    public override void Configure()
    {
        Get("catalog/campaign-events");
        Policies(AuthorizationPolicies.AccessAsUser);
        Summary(summary =>
        {
            summary.Summary = "Gets catalog campaign events.";
            summary.Response<CatalogItemsResponse<CatalogCampaign>>(StatusCodes.Status200OK, "The active catalog campaign events.");
            summary.Response(StatusCodes.Status304NotModified, "The filtered campaign event result has not changed.");
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var query = CatalogQuery.From(HttpContext);
        var items = catalog.Current.CampaignEvents.AsEnumerable();

        items = query.ApplySearch(items, campaign => [campaign.Id, campaign.Name, campaign.DisplayName, campaign.Faction]);
        items = query.ApplyEquals(items, "groupType", campaign => campaign.GroupType);
        items = query.ApplyEquals(items, "difficulty", campaign => campaign.Difficulty);

        var response = CatalogEndpointSender.CreateResponse(
            HttpContext,
            catalog.Current,
            CatalogDatasets.Campaigns,
            items,
            query
        );

        return response is null ? Send.NotModifiedAsync(ct) : Send.OkAsync(response, ct);
    }
}

public sealed class GetCatalogCampaignBattlesEndpoint(ICatalogProvider catalog)
    : EndpointWithoutRequest<CatalogItemsResponse<CatalogCampaignBattle>>
{
    public override void Configure()
    {
        Get("catalog/campaign-battles");
        Policies(AuthorizationPolicies.AccessAsUser);
        Summary(summary =>
        {
            summary.Summary = "Gets catalog campaign battles.";
            summary.Response<CatalogItemsResponse<CatalogCampaignBattle>>(StatusCodes.Status200OK, "The active catalog campaign battles.");
            summary.Response(StatusCodes.Status304NotModified, "The filtered campaign battle result has not changed.");
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var query = CatalogQuery.From(HttpContext);
        var items = catalog.Current.CampaignBattles.AsEnumerable();

        items = query.ApplySearch(items, battle => [battle.Id, battle.Campaign, battle.CampaignType]);
        items = query.ApplyEquals(items, "campaignId", battle => battle.Campaign);
        items = query.ApplyEquals(items, "campaignType", battle => battle.CampaignType);

        if (query.TryGet("rewardId", out var rewardId))
        {
            items = items.Where(battle => battle.Rewards.AllRewards.Any(reward =>
                string.Equals(reward.Id, rewardId, StringComparison.OrdinalIgnoreCase)
            ));
        }

        var response = CatalogEndpointSender.CreateResponse(
            HttpContext,
            catalog.Current,
            CatalogDatasets.CampaignBattles,
            items,
            query
        );

        return response is null ? Send.NotModifiedAsync(ct) : Send.OkAsync(response, ct);
    }
}

public sealed class GetCatalogLresEndpoint(ICatalogProvider catalog)
    : EndpointWithoutRequest<CatalogItemsResponse<CatalogLre>>
{
    public override void Configure()
    {
        Get("catalog/lres");
        Policies(AuthorizationPolicies.AccessAsUser);
        Summary(summary =>
        {
            summary.Summary = "Gets catalog legendary release events.";
            summary.Response<CatalogItemsResponse<CatalogLre>>(StatusCodes.Status200OK, "The active catalog LREs.");
            summary.Response(StatusCodes.Status304NotModified, "The filtered LRE result has not changed.");
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var query = CatalogQuery.From(HttpContext);
        var items = catalog.Current.Lres.AsEnumerable();

        items = query.ApplySearch(items, lre => [lre.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), lre.Name, lre.UnitSnowprintId]);
        items = query.ApplyEquals(items, "unitId", lre => lre.UnitSnowprintId);

        if (query.TryGet("finished", out var finished) && bool.TryParse(finished, out var parsedFinished))
        {
            items = items.Where(lre => lre.Finished == parsedFinished);
        }

        var response = CatalogEndpointSender.CreateResponse(
            HttpContext,
            catalog.Current,
            CatalogDatasets.Lres,
            items.OrderBy(lre => lre.Id),
            query
        );

        return response is null ? Send.NotModifiedAsync(ct) : Send.OkAsync(response, ct);
    }
}
