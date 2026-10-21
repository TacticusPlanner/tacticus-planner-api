using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.GameCatalog;

/// <summary>
/// The served dataset envelopes of a catalog snapshot and their content addresses. The envelope hash is the
/// SHA-256 of the complete unencoded envelope (release metadata and data), so it changes with any release — even a
/// metadata-only one — and it is the immutable dataset URL, the ETag base and the hashed route's 404 check. The
/// manifest's own <c>hash</c> per dataset stays the payload digest.
/// </summary>
public static class ServedDatasets
{
    private static readonly Dictionary<string, Func<GameCatalogSnapshot, object>> Payloads =
        new Dictionary<string, Func<GameCatalogSnapshot, object>>(StringComparer.Ordinal)
        {
            [GameCatalogDatasets.Characters] = snapshot => snapshot.CharacterViews,
            [GameCatalogDatasets.Npcs] = snapshot => snapshot.NpcList,
            [GameCatalogDatasets.Mows] = snapshot => snapshot.MowList,
            [GameCatalogDatasets.MowUpgradeCostsServed] = snapshot => snapshot.MowUpgradeCostViews,
            [GameCatalogDatasets.CharacterAbilityCostsServed] = snapshot => snapshot.CharacterAbilityCostViews,
            [GameCatalogDatasets.MythicQuestsServed] = snapshot => snapshot.MythicQuestViews,
            [GameCatalogDatasets.AscensionCostsServed] = snapshot => snapshot.AscensionCostViews,
            [GameCatalogDatasets.UnlockShardCostsServed] = snapshot => snapshot.UnlockShardCostViews,
            [GameCatalogDatasets.OnslaughtRewards] = snapshot => snapshot.OnslaughtRewards,
            [GameCatalogDatasets.Upgrades] = snapshot => snapshot.UpgradeViews,
            [GameCatalogDatasets.Equipment] = snapshot => snapshot.EquipmentViews,
            [GameCatalogDatasets.CampaignBattles] = snapshot => snapshot.CampaignBattleViews,
            [GameCatalogDatasets.CampaignDefinitions] = snapshot => snapshot.CampaignDefinitionViews,
            [GameCatalogDatasets.Lres] = snapshot => snapshot.LreViews,
            [GameCatalogDatasets.LreBattles] = snapshot => snapshot.LreBattleViews,
            [GameCatalogDatasets.HordeModeTiers] = snapshot => snapshot.HordeModeTiersViews,
            [GameCatalogDatasets.UnitPower] = snapshot => snapshot.UnitPowerViews,
            [GameCatalogDatasets.EventDefinitionsServed] = snapshot => snapshot.EventDefinitionViews,
            [GameCatalogDatasets.EventsCalendar] = snapshot => snapshot.EventsCalendar,
            [GameCatalogDatasets.Shops] = snapshot => snapshot.ShopViews,
            [GameCatalogDatasets.RaidBosses] = snapshot => snapshot.RaidBossesView,
            [GameCatalogDatasets.GuildRaidMeta] = snapshot => snapshot.GuildRaidMetaView,
        };

    private static readonly ConditionalWeakTable<GameCatalogSnapshot, ConcurrentDictionary<string, string>> Hashes = new();
    private static readonly ConditionalWeakTable<GameCatalogSnapshot, ServedManifest> Manifests = new();

    public static GameCatalogDatasetEnvelope<TPayload> Envelope<TPayload>(GameCatalogSnapshot snapshot, string key) =>
        new(
            snapshot.Version,
            snapshot.SchemaVersion,
            snapshot.GameVersion,
            snapshot.SourceHash,
            key,
            snapshot.DatasetHashes[key],
            (TPayload)Payloads[key](snapshot));

    public static string EnvelopeHash(GameCatalogSnapshot snapshot, string key) =>
        Hashes.GetValue(snapshot, _ => new()).GetOrAdd(key, _ => Digest(Envelope<object>(snapshot, key)));

    /// <summary>The manifest as served: each dataset's <c>url</c> is its immutable content address
    /// (<c>{prefix}/{key}/{envelopeHash}</c>), and the manifest's own validator covers those URLs.</summary>
    public static ServedManifest Manifest(GameCatalogSnapshot snapshot) =>
        Manifests.GetValue(snapshot, _ =>
        {
            var manifest = snapshot.Manifest with
            {
                Datasets = snapshot.Manifest.Datasets
                    .Select(dataset => dataset with { Url = $"{dataset.Url}/{EnvelopeHash(snapshot, dataset.Key)}" })
                    .ToArray(),
            };
            return new ServedManifest(manifest, Digest(manifest));
        });

    private static string Digest(object value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.Web)));
}

/// <param name="Hash">Digest of the served manifest, its strong validator.</param>
public sealed record ServedManifest(GameCatalogManifest Manifest, string Hash);
