using FastEndpoints;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Net.Http.Headers;
using TacticusPlanner.Api.Http;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.GameCatalog;

/// <summary>
/// Serves one whole denormalized dataset (no route parameter). The payload is already self-contained
/// (reference tables inlined), so the client consumes it without any cross-dataset joins. Two routes: the
/// manifest's content address <c>{dataset}/{envelopeHash}</c> is immutable (a hash that is not the current
/// envelope's is a 404), and the bare <c>{dataset}</c> route revalidates. The strong <c>ETag</c> covers the
/// complete envelope (release metadata and data) and the negotiated content encoding.
/// </summary>
public abstract class ServedDatasetEndpoint<TPayload>(IGameCatalogProvider catalog, string datasetKey)
    : EndpointWithoutRequest<GameCatalogDatasetEnvelope<TPayload>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var snapshot = catalog.Current;
        var envelopeHash = ServedDatasets.EnvelopeHash(snapshot, datasetKey);
        var requestedHash = Route<string>("envelopeHash", isRequired: false);
        if (requestedHash is not null && requestedHash != envelopeHash)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        // ponytail: the ETag is the identity-JSON digest plus an encoding token, not a digest of the compressed
        // bytes (the compression middleware runs after the endpoint); distinct per encoding, stable per content.
        var encoding = HttpContext.RequestServices.GetRequiredService<IResponseCompressionProvider>()
            .GetCompressionProvider(HttpContext)?.EncodingName;
        var etag = ETagHelper.CreateEtag(encoding is null ? envelopeHash : $"{envelopeHash}-{encoding}");

        HttpContext.Response.Headers.CacheControl = requestedHash is null
            ? "public, max-age=0, must-revalidate"
            : "public, max-age=31536000, immutable";
        HttpContext.Response.Headers.Vary = HeaderNames.AcceptEncoding;

        if (ETagHelper.TryApplyNotModified(HttpContext, etag))
        {
            await Send.NotModifiedAsync(ct);
            return;
        }

        HttpContext.Response.Headers.ETag = etag;
        await Send.OkAsync(ServedDatasets.Envelope<TPayload>(snapshot, datasetKey), ct);
    }

    protected void ConfigureServed(string summary, string okDescription)
    {
        AllowAnonymous();
        Summary(endpointSummary =>
        {
            endpointSummary.Summary = summary;
            endpointSummary.Response<GameCatalogDatasetEnvelope<TPayload>>(StatusCodes.Status200OK, okDescription);
            endpointSummary.Response(StatusCodes.Status304NotModified, "The dataset has not changed.");
            endpointSummary.Response(StatusCodes.Status404NotFound, "The envelope hash is not the current release's.");
        });
    }
}

public sealed class GetGameCatalogCharactersEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogCharacterView>>(catalog, GameCatalogDatasets.Characters)
{
    public override void Configure()
    {
        Get("game-catalog/characters", "game-catalog/characters/{envelopeHash}");
        ConfigureServed("Gets game catalog characters.", "All characters with shard farm locations and eligible equipment.");
    }
}

public sealed class GetGameCatalogNpcsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogNpc>>(catalog, GameCatalogDatasets.Npcs)
{
    public override void Configure()
    {
        Get("game-catalog/npcs", "game-catalog/npcs/{envelopeHash}");
        ConfigureServed("Gets game catalog NPCs.", "All non-playable units.");
    }
}

public sealed class GetGameCatalogMowsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogMow>>(catalog, GameCatalogDatasets.Mows)
{
    public override void Configure()
    {
        Get("game-catalog/mows", "game-catalog/mows/{envelopeHash}");
        ConfigureServed("Gets game catalog machines of war.", "All machines of war.");
    }
}

public sealed class GetGameCatalogMowUpgradeCostsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogMowUpgradeCostView>>(catalog, GameCatalogDatasets.MowUpgradeCostsServed)
{
    public override void Configure()
    {
        Get("game-catalog/mow-upgrade-costs", "game-catalog/mow-upgrade-costs/{envelopeHash}");
        ConfigureServed("Gets the machine-of-war upgrade-cost ladder.", "The shared per-level upgrade-cost ladder for all machines of war.");
    }
}

public sealed class GetGameCatalogCharacterAbilityCostsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogCharacterAbilityCostView>>(catalog, GameCatalogDatasets.CharacterAbilityCostsServed)
{
    public override void Configure()
    {
        Get("game-catalog/character-ability-costs", "game-catalog/character-ability-costs/{envelopeHash}");
        ConfigureServed("Gets the character ability cost ladder.", "The shared per-level gold and ability-badge cost ladder for all character abilities.");
    }
}

public sealed class GetGameCatalogMythicQuestsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogMythicQuestsView>>(catalog, GameCatalogDatasets.MythicQuestsServed)
{
    public override void Configure()
    {
        Get("game-catalog/mythic-quests", "game-catalog/mythic-quests/{envelopeHash}");
        ConfigureServed("Gets game catalog mythic quests.", "Each character's mythic quests with tasks and structured rewards.");
    }
}

public sealed class GetGameCatalogQuestEventsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogQuestEventView>>(catalog, GameCatalogDatasets.QuestEvents)
{
    public override void Configure()
    {
        Get("game-catalog/quest-events", "game-catalog/quest-events/{envelopeHash}");
        ConfigureServed("Gets game catalog quest events.", "Each Quest Event's featured hero, factions and 17 battles with enemies, objectives and drops.");
    }
}

public sealed class GetGameCatalogQuestCratesEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogQuestCrate>>(catalog, GameCatalogDatasets.QuestCrates)
{
    public override void Configure()
    {
        Get("game-catalog/quest-crates", "game-catalog/quest-crates/{envelopeHash}");
        ConfigureServed("Gets the Quest Event crate track.", "The shared 17-crate point track with each crate's points and rewards.");
    }
}

public sealed class GetGameCatalogAscensionCostsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogAscensionCostView>>(catalog, GameCatalogDatasets.AscensionCostsServed)
{
    public override void Configure()
    {
        Get("game-catalog/ascension-costs", "game-catalog/ascension-costs/{envelopeHash}");
        ConfigureServed("Gets the ascension-orb/shard cost ladder.", "The shared per-progression-step ascension cost ladder (shards, mythic shards, orbs) for all characters.");
    }
}

public sealed class GetGameCatalogUnlockShardCostsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogUnlockShardCostView>>(catalog, GameCatalogDatasets.UnlockShardCostsServed)
{
    public override void Configure()
    {
        Get("game-catalog/unlock-shard-costs", "game-catalog/unlock-shard-costs/{envelopeHash}");
        ConfigureServed("Gets the per-rarity character unlock shard cost table.", "The shard count required to unlock a character of each rarity.");
    }
}

public sealed class GetGameCatalogOnslaughtRewardsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogOnslaughtReward>>(catalog, GameCatalogDatasets.OnslaughtRewards)
{
    public override void Configure()
    {
        Get("game-catalog/onslaught-rewards", "game-catalog/onslaught-rewards/{envelopeHash}");
        ConfigureServed("Gets the Onslaught reward table.", "Reward ranges by sector, tier, and rarity.");
    }
}

public sealed class GetGameCatalogUpgradesEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogUpgradeView>>(catalog, GameCatalogDatasets.Upgrades)
{
    public override void Configure()
    {
        Get("game-catalog/upgrades", "game-catalog/upgrades/{envelopeHash}");
        ConfigureServed("Gets game catalog upgrade materials.", "All upgrades with farm locations and expanded recipes.");
    }
}

public sealed class GetGameCatalogEquipmentEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogEquipmentView>>(catalog, GameCatalogDatasets.Equipment)
{
    public override void Configure()
    {
        Get("game-catalog/equipment", "game-catalog/equipment/{envelopeHash}");
        ConfigureServed("Gets game catalog equipment.", "All equipment with the matched per-rarity upgrade-cost ladder inlined.");
    }
}

public sealed class GetGameCatalogCampaignBattlesEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogCampaignBattleView>>(catalog, GameCatalogDatasets.CampaignBattles)
{
    public override void Configure()
    {
        Get("game-catalog/campaign-battles", "game-catalog/campaign-battles/{envelopeHash}");
        ConfigureServed("Gets game catalog campaign battles.", "All campaign battles (keyed by battle id, each with its campaignGroupId) with inlined reward drop chances.");
    }
}

public sealed class GetGameCatalogCampaignDefinitionsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogCampaignDefinitionView>>(catalog, GameCatalogDatasets.CampaignDefinitions)
{
    public override void Configure()
    {
        Get("game-catalog/campaign-definitions", "game-catalog/campaign-definitions/{envelopeHash}");
        ConfigureServed("Gets game catalog campaign definitions.", "All campaign groups (keyed by groupId) with metadata and the ids of their battles.");
    }
}

public sealed class GetGameCatalogLresEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogLreView>>(catalog, GameCatalogDatasets.Lres)
{
    public override void Configure()
    {
        Get("game-catalog/lres", "game-catalog/lres/{envelopeHash}");
        ConfigureServed("Gets game catalog legendary release events.", "All legendary release events (keyed by id) with per-track metadata and the ids of their battles.");
    }
}

public sealed class GetGameCatalogLreBattlesEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogLreBattleView>>(catalog, GameCatalogDatasets.LreBattles)
{
    public override void Configure()
    {
        Get("game-catalog/lre-battles", "game-catalog/lre-battles/{envelopeHash}");
        ConfigureServed("Gets game catalog LRE battles.", "All LRE battles (keyed \"{lreId}-{track}-{number}\", each tagged with its lreId and track) with their waves.");
    }
}

public sealed class GetGameCatalogHordeModeTiersEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogHordeModeTiers>>(catalog, GameCatalogDatasets.HordeModeTiers)
{
    public override void Configure()
    {
        Get("game-catalog/horde-mode-tiers", "game-catalog/horde-mode-tiers/{envelopeHash}");
        ConfigureServed(
            "Gets the horde-mode character tier list.",
            "The single S/A/B/C/D tier list of characters for horde modes, with each tier's power multiplier ; a character may override its tier's multiplier, and one not listed is unrated, multiplier 1.0).");
    }
}

public sealed class GetGameCatalogUnitPowerEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogUnitPower>>(catalog, GameCatalogDatasets.UnitPower)
{
    public override void Configure()
    {
        Get("game-catalog/unit-power", "game-catalog/unit-power/{envelopeHash}");
        ConfigureServed(
            "Gets the unit power tables.",
            "The single record of power tables the in-game power formula reads: hero and MoW progression multipliers, damage profile, trait and ability power modifiers (percent; a trait or ability not listed is 100), ability power curves, and each character's and MoW's power multiplier and per-rank upgrade stat increases.");
    }
}

public sealed class GetGameCatalogEventDefinitionsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogEventDefinition>>(catalog, GameCatalogDatasets.EventDefinitionsServed)
{
    public override void Configure()
    {
        Get("game-catalog/event-definitions", "game-catalog/event-definitions/{envelopeHash}");
        ConfigureServed("Gets game catalog event definitions.", "Reusable event mechanics (scoring, applicable game modes, recurrence, required parameters) — no display text or icon; clients resolve those from each definition's id.");
    }
}

public sealed class GetGameCatalogEventsCalendarEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyDictionary<string, IReadOnlyList<GameCatalogEventsCalendarEntry>>>(catalog, GameCatalogDatasets.EventsCalendar)
{
    public override void Configure()
    {
        Get("game-catalog/events-calendar", "game-catalog/events-calendar/{envelopeHash}");
        ConfigureServed(
            "Gets the game events calendar.",
            "Date-indexed (ISO date key) event occurrences and projected placeholders; an entry spanning multiple dates is repeated under every date it spans.");
    }
}

public sealed class GetGameCatalogShopsEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<IReadOnlyList<GameCatalogShopView>>(catalog, GameCatalogDatasets.Shops)
{
    public override void Configure()
    {
        Get("game-catalog/shops", "game-catalog/shops/{envelopeHash}");
        ConfigureServed(
            "Gets the always-on daily shops.",
            "One record per daily shop (guild, war, rogue-trader, crusade) with its rotating slots, structured rewards/costs, explicit day-of-week availability, and opaque lock ids — no display text or icons.");
    }
}

public sealed class GetGameCatalogRaidBossesEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<GameCatalogRaidBossesView>(catalog, GameCatalogDatasets.RaidBosses)
{
    public override void Configure()
    {
        Get("game-catalog/raid-bosses", "game-catalog/raid-bosses/{envelopeHash}");
        ConfigureServed(
            "Gets the raid bosses and raid-boss primes.",
            "The season-config rotation, every raid boss and raid-boss prime (progression ladder, weapons, ability/trait ids), and the season configs whose encounters carry their referenced unit-set id, field-npc ids, and modifier definitions inlined — no display text or icons.");
    }
}

public sealed class GetGameCatalogGuildRaidMetaEndpoint(IGameCatalogProvider catalog)
    : ServedDatasetEndpoint<GameCatalogGuildRaidMetaView>(catalog, GameCatalogDatasets.GuildRaidMeta)
{
    public override void Configure()
    {
        Get("game-catalog/guild-raid-meta", "game-catalog/guild-raid-meta/{envelopeHash}");
        ConfigureServed(
            "Gets curated Guild Raid Meta recommendations.",
            "Curated exact Guild Raid Boss teams, Comp guidance, and source/update ids — no display names, icons, or source URLs.");
    }
}
