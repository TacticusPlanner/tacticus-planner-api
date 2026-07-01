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

    private readonly record struct RewardLocation(string BattleId, string Difficulty, bool Guaranteed, string? ChanceId);

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
                    Add(reward.Id, new RewardLocation(battle.Id, battle.Difficulty, true, null));
                }

                foreach (var reward in battle.Rewards.Potential)
                {
                    Add(reward.Id, new RewardLocation(battle.Id, battle.Difficulty, false, reward.ChanceId));
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
        Dictionary<string, GameCatalogDropChance> dropChanceById)
    {
        if (!rewardLocations.TryGetValue(rewardId, out var locations))
        {
            return [];
        }

        return locations.Select(location =>
        {
            if (location.Guaranteed || location.ChanceId is null
                || !dropChanceById.TryGetValue(location.ChanceId, out var chance))
            {
                return new GameCatalogFarmLocation(location.BattleId, location.Difficulty, location.Guaranteed,
                    location.Guaranteed ? null : location.ChanceId, null, null, null);
            }

            return new GameCatalogFarmLocation(location.BattleId, location.Difficulty, false,
                location.ChanceId, chance.Numerator, chance.Denominator, chance.EffectiveRate);
        }).ToArray();
    }
}
