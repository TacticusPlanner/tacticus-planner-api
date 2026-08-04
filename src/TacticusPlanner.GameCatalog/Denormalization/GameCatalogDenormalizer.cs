using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

/// <summary>
/// Builds the consolidated, denormalized served datasets from the raw source collections: cross-references
/// (shard/upgrade farm locations with inlined drop chances, eligible equipment, recursively nested
/// recipes, per-track available units) are resolved server-side so the client never joins.
///
/// The builders are split per entity across the <c>Denormalization</c> folder; this file holds the
/// shared reward-location/drop-chance helpers they all use.
/// </summary>
internal static partial class GameCatalogDenormalizer
{
    private static readonly string[] ShardPrefixes = ["shards_", "mythicShards_"];

    private readonly record struct RewardLocation(string BattleId, string Type, bool Challenge, bool Guaranteed, string? ChanceId);

    private static Dictionary<string, GameCatalogDropChance> BuildDropChanceIndex(IReadOnlyList<GameCatalogDropChance> dropChances) =>
        dropChances
            .GroupBy(chance => chance.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static Dictionary<string, List<RewardLocation>> BuildRewardLocations(
        IReadOnlyDictionary<string, GameCatalogCampaignGroup> campaignGroups)
    {
        var locations = new Dictionary<string, List<RewardLocation>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in campaignGroups.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value))
        {
            foreach (var battle in group.Battles)
            {
                foreach (var reward in battle.Rewards.Guaranteed)
                {
                    Add(reward.Id, new RewardLocation(battle.Id, battle.Type, battle.Challenge, true, null));
                }

                foreach (var reward in battle.Rewards.Potential)
                {
                    Add(reward.Id, new RewardLocation(battle.Id, battle.Type, battle.Challenge, false, reward.ChanceId));
                }
            }
        }

        return locations;

        void Add(string rewardId, RewardLocation location)
        {
            if (string.IsNullOrWhiteSpace(rewardId) || string.Equals(rewardId, "gold", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!locations.TryGetValue(rewardId, out var list))
            {
                list = [];
                locations[rewardId] = list;
            }

            list.Add(location);
        }
    }

    private static GameCatalogFarmLocation[] ResolveLocations(
        string rewardId,
        Dictionary<string, List<RewardLocation>> rewardLocations,
        Dictionary<string, GameCatalogDropChance> dropChanceById,
        bool isMythic = false)
    {
        if (!rewardLocations.TryGetValue(rewardId, out var locations))
        {
            return [];
        }

        return locations
            .GroupBy(location => location.BattleId, StringComparer.Ordinal)
            .Select(group =>
        {
            var groupedLocations = group.ToArray();
            if (groupedLocations.Length == 1)
            {
                return ResolveLocation(groupedLocations[0]);
            }

            var first = groupedLocations[0];
            var guaranteedCount = groupedLocations.Count(location => location.Guaranteed);
            var guaranteed = guaranteedCount > 0;
            var potentialLocations = groupedLocations.Where(location => !location.Guaranteed).ToArray();
            var resolvedPotentialRates = potentialLocations
                .Select(location => location.ChanceId is not null
                    && dropChanceById.TryGetValue(location.ChanceId, out var chance)
                        ? chance.EffectiveRate
                        : (double?)null)
                .ToArray();
            var effectiveRate = resolvedPotentialRates.All(rate => rate.HasValue)
                ? guaranteedCount + resolvedPotentialRates.Sum(rate => rate!.Value)
                : (double?)null;

            return new GameCatalogFarmLocation(
                first.BattleId,
                first.Type,
                first.Challenge,
                guaranteed,
                null,
                null,
                null,
                effectiveRate,
                isMythic);
        })
            .ToArray();

        GameCatalogFarmLocation ResolveLocation(RewardLocation location)
        {
            if (location.Guaranteed || location.ChanceId is null
                || !dropChanceById.TryGetValue(location.ChanceId, out var chance))
            {
                return new GameCatalogFarmLocation(location.BattleId, location.Type, location.Challenge, location.Guaranteed,
                    location.Guaranteed ? null : location.ChanceId, null, null, null, isMythic);
            }

            return new GameCatalogFarmLocation(location.BattleId, location.Type, location.Challenge, false,
                location.ChanceId, chance.Numerator, chance.Denominator, chance.EffectiveRate, isMythic);
        }
    }
}
