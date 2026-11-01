using System.Globalization;
using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine Machine of War event (Incursion).</summary>
    public const string DatamineMowEventPath = "gameconfig/liveEvents/mowEvent.json";

    private const string IncursionBattleNode = "incursionBattle";
    private const string IncursionEnhancementNode = "incursionEnhancement";
    private const string IncursionChestNode = "chest";

    /// <summary>
    /// Builds one record per MoW of the datamine Incursion event, ordered by first availability, then MoW id: the
    /// event's tokens, team rule, Oh So Close pricing and the enhancements the MoW's run maps use, and per tier its
    /// battles (rewards, enemies grouped by npc and level) and run-map nodes with each chest's rewards inlined.
    /// </summary>
    /// <exception cref="InvalidOperationException">The event is missing or a node, battle or chest does not resolve.</exception>
    public static IReadOnlyList<GameCatalogIncursion> ReadDatamineIncursions(
        IReadOnlyList<DatamineMowEvent> events,
        IReadOnlyDictionary<string, JsonElement> chests)
    {
        if (events is not [var mowEvent])
        {
            throw IncursionError("event", $"{DatamineMowEventPath} must hold exactly one event.");
        }

        if (mowEvent.Mows is not { Count: > 0 } mows
            || mowEvent.OhSoCloseOffer is not { Unlock: not null, Ascension: not null } ohSoClose
            || mowEvent.Enhancements is not { } enhancements)
        {
            throw IncursionError("event", "it needs mows, an ohSoCloseOffer and enhancements.");
        }

        if (mowEvent.StartStamina < 0 || mowEvent.MaxStamina <= 0 || mowEvent.StaminaRegenerationTime <= 0
            || mowEvent.StaminaRegenerationAmount <= 0 || mowEvent.StaminaConsumptionOnStart <= 0
            || mowEvent.MinUnitsOfGrandAllianceNeeded < 0)
        {
            throw IncursionError("event", "its stamina and team values must be positive.");
        }

        var tokens = new GameCatalogIncursionTokens(
            mowEvent.StartStamina,
            mowEvent.MaxStamina,
            mowEvent.StaminaRegenerationTime,
            mowEvent.StaminaRegenerationAmount,
            mowEvent.StaminaConsumptionOnStart);

        return mows
            .Select(mow =>
            {
                var id = mow.UnitId;
                if (mow.Tiers is not { Count: > 0 } tiers)
                {
                    throw IncursionError(id, "it has no tiers.");
                }

                var served = tiers.Select(tier => ReadIncursionTier(id, tier, enhancements, chests)).ToArray();
                if (!served.Select(tier => tier.Index).SequenceEqual(Enumerable.Range(0, served.Length)))
                {
                    throw IncursionError(id, "its tier indices must run 0..N-1 in order.");
                }

                var used = served
                    .SelectMany(tier => tier.Nodes)
                    .Select(node => node.EnhancementId)
                    .OfType<string>()
                    .ToHashSet(StringComparer.Ordinal);
                return new GameCatalogIncursion(
                    id,
                    DateTimeOffset.FromUnixTimeMilliseconds(mow.AvailableFrom).UtcDateTime
                        .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    mow.New,
                    mow.Updated,
                    mow.AllowedFactions ?? [],
                    tokens,
                    mowEvent.MinUnitsOfGrandAllianceNeeded,
                    ohSoClose,
                    new SortedDictionary<string, GameCatalogIncursionEnhancement>(
                        used.ToDictionary(
                            enhancementId => enhancementId,
                            enhancementId =>
                            {
                                var enhancement = enhancements[enhancementId];
                                return new GameCatalogIncursionEnhancement(
                                    enhancement.Type,
                                    enhancement.AbilityId,
                                    enhancement.UnitAmount,
                                    enhancement.Stacks);
                            },
                            StringComparer.Ordinal),
                        StringComparer.Ordinal),
                    served);
            })
            .OrderBy(incursion => incursion.AvailableFromUtc, StringComparer.Ordinal)
            .ThenBy(incursion => incursion.MowId, StringComparer.Ordinal)
            .ToArray();
    }

    private static GameCatalogIncursionTier ReadIncursionTier(
        string id,
        DatamineMowEventTier tier,
        IReadOnlyDictionary<string, DatamineMowEventEnhancement> enhancements,
        IReadOnlyDictionary<string, JsonElement> chests)
    {
        var where = $"{id} tier {tier.Index.ToString(CultureInfo.InvariantCulture)}";
        if (tier.Battles is not { Count: > 0 } battles || tier.Nodes is not { Count: > 0 } nodes)
        {
            throw IncursionError(where, "it needs battles and nodes.");
        }

        var nodeIndices = nodes.Select(node => node.Index).ToArray();
        if (!nodeIndices.SequenceEqual(Enumerable.Range(0, nodeIndices.Length)))
        {
            throw IncursionError(where, "its node indices must run 0..N-1 in order.");
        }

        var battleIds = battles.Select(battle => battle.BattleId).ToHashSet(StringComparer.Ordinal);
        if (battleIds.Count != battles.Count)
        {
            throw IncursionError(where, "its battle ids repeat.");
        }

        var challengeIds = new HashSet<string>(StringComparer.Ordinal);
        var servedNodes = nodes.Select(node =>
        {
            var unlockedBy = (node.UnlockedBy ?? []).Select(parent => parent.Index).ToArray();
            var exclusiveWith = node.ExclusiveWith ?? [];
            if (unlockedBy.Any(parent => parent < 0 || parent >= nodes.Count || nodes[parent].NodeDepth >= node.NodeDepth)
                || exclusiveWith.Any(other => other < 0 || other >= nodes.Count || other == node.Index)
                || (node.NodeDepth > 0) != (unlockedBy.Length > 0))
            {
                throw IncursionError(where, $"node {node.Index.ToString(CultureInfo.InvariantCulture)} has an invalid unlockedBy or exclusiveWith.");
            }

            switch (node.Type)
            {
                case IncursionBattleNode:
                    if (node.BattleId is not { } battleId || !battleIds.Contains(battleId))
                    {
                        throw IncursionError(where, $"node {node.Index.ToString(CultureInfo.InvariantCulture)} names an unknown battle '{node.BattleId}'.");
                    }

                    if (node.BattleType == "challenge")
                    {
                        challengeIds.Add(battleId);
                    }

                    return new GameCatalogIncursionNode(node.Index, node.NodeDepth, "battle", node.NodeName, battleId, null, null, unlockedBy, exclusiveWith);
                case IncursionEnhancementNode:
                    if (node.EnhancementId is not { } enhancementId || !enhancements.ContainsKey(enhancementId))
                    {
                        throw IncursionError(where, $"node {node.Index.ToString(CultureInfo.InvariantCulture)} names an unknown enhancement '{node.EnhancementId}'.");
                    }

                    return new GameCatalogIncursionNode(node.Index, node.NodeDepth, "enhancement", node.NodeName, null, enhancementId, null, unlockedBy, exclusiveWith);
                case IncursionChestNode:
                    return new GameCatalogIncursionNode(
                        node.Index, node.NodeDepth, "chest", node.NodeName, null, null,
                        ReadIncursionChest(where, node.ChestId, chests), unlockedBy, exclusiveWith);
                default:
                    throw IncursionError(where, $"node {node.Index.ToString(CultureInfo.InvariantCulture)} has unknown type '{node.Type}'.");
            }
        }).ToArray();

        var servedBattles = battles.Select(battle =>
        {
            var loot = battle.Loot;
            if (loot is not { Star1: { Length: > 0 } star1, Star2: { Length: > 0 } star2, Star3: { Length: > 0 } star3 }
                || string.IsNullOrEmpty(battle.BoardId)
                || battle.DifficultyMultiplier <= 0)
            {
                throw IncursionError(where, $"battle '{battle.BattleId}' needs a board, a positive difficulty and three star rewards.");
            }

            return new GameCatalogIncursionBattle(
                battle.BattleId,
                battle.BoardId,
                challengeIds.Contains(battle.BattleId),
                battle.DifficultyMultiplier,
                battle.LightningVictory,
                loot.Base ?? [],
                [star1, star2, star3],
                ReadIncursionEnemies(where, battle));
        }).ToArray();

        return new GameCatalogIncursionTier(tier.Index, tier.Rarity, servedBattles, servedNodes);
    }

    // Enemies "npcId:level" grouped by npc and level, in order of first appearance.
    private static GameCatalogIncursionEnemy[] ReadIncursionEnemies(string where, DatamineMowEventBattle battle)
    {
        var groups = new List<(string NpcId, int Level, int Count)>();
        foreach (var enemy in battle.Enemies ?? [])
        {
            var parts = enemy.Split(':');
            if (parts.Length != 2 || parts[0].Length == 0
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var level))
            {
                throw IncursionError(where, $"battle '{battle.BattleId}' enemy '{enemy}' is not npcId:level.");
            }

            var at = groups.FindIndex(group => group.NpcId == parts[0] && group.Level == level);
            if (at < 0)
            {
                groups.Add((parts[0], level, 1));
            }
            else
            {
                groups[at] = groups[at] with { Count = groups[at].Count + 1 };
            }
        }

        if (groups.Count == 0)
        {
            throw IncursionError(where, $"battle '{battle.BattleId}' has no enemies.");
        }

        return groups.Select(group => new GameCatalogIncursionEnemy(group.NpcId, group.Level, group.Count)).ToArray();
    }

    private static GameCatalogIncursionChest ReadIncursionChest(
        string where,
        string? chestId,
        IReadOnlyDictionary<string, JsonElement> chests)
    {
        if (chestId is null || !chests.TryGetValue(chestId, out var chest)
            || chest.ValueKind != JsonValueKind.Array || chest.GetArrayLength() != 1)
        {
            throw IncursionError(where, $"chest '{chestId}' is missing from chests.json or is not a single fixed reward list.");
        }

        var entry = chest[0];
        var type = entry.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        if (type is not { Length: > 0 } || !entry.TryGetProperty("rewards", out var rewards) || rewards.ValueKind != JsonValueKind.Array)
        {
            throw IncursionError(where, $"chest '{chestId}' needs a type and rewards.");
        }

        return new GameCatalogIncursionChest(
            chestId,
            type,
            rewards.EnumerateArray().Select(reward => reward.GetString() ?? "").ToArray());
    }

    private static InvalidOperationException IncursionError(string id, string message) =>
        new($"Dataset '{GameCatalogDatasets.Incursions}' incursion '{id}': {message}");
}
