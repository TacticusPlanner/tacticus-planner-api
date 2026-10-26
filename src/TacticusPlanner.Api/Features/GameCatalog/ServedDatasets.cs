using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.ResponseCompression;
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
            [GameCatalogDatasets.QuestEvents] = snapshot => snapshot.QuestEventViews,
            [GameCatalogDatasets.QuestCrates] = snapshot => snapshot.QuestCrateViews,
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
    private static readonly ConditionalWeakTable<GameCatalogSnapshot, ConcurrentDictionary<(string Key, string? Encoding), byte[]>> Bodies = new();

    /// <summary>The envelope's response body: serialized once per snapshot with the API's JSON options, and
    /// compressed once per content encoding (<paramref name="compression"/> is the negotiated provider, null for
    /// identity). The snapshot is immutable, so every 200 for a dataset is the same bytes.</summary>
    // ponytail: caches the whole served catalog per encoding (~5 MB identity + ~0.5 MB each compressed); fine
    // for one live snapshot.
    public static byte[] Body<TPayload>(GameCatalogSnapshot snapshot, string key, ICompressionProvider? compression, JsonSerializerOptions options)
    {
        var bodies = Bodies.GetValue(snapshot, _ => new());
        var identity = bodies.GetOrAdd((key, null), _ => JsonSerializer.SerializeToUtf8Bytes(Envelope<TPayload>(snapshot, key), options));
        if (compression is null)
        {
            return identity;
        }

        return bodies.GetOrAdd((key, compression.EncodingName), _ =>
        {
            using var buffer = new MemoryStream();
            using (var stream = compression.CreateStream(buffer))
            {
                stream.Write(identity);
            }

            return buffer.ToArray();
        });
    }

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
