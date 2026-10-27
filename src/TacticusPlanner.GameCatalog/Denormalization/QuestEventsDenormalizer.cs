using System.Globalization;
using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    internal static readonly string[] QuestTierRarities = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];

    // Units deployed per battle (featured hero included), by tier and 1-based position in the tier. Confirmed by
    // Severyn and Nubari's in-game screens; spawnPointsSet is not a slot count.
    private static readonly Dictionary<string, int[]> QuestDeploySlots = new Dictionary<string, int[]>(StringComparer.Ordinal)
    {
        ["Common"] = [3, 4, 5],
        ["Uncommon"] = [3, 4, 5],
        ["Rare"] = [3, 3, 3, 4, 5],
        ["Epic"] = [3, 4, 5],
        ["Legendary"] = [5, 5],
        ["Mythic"] = [5],
    };

    public static IReadOnlyList<GameCatalogQuestEventView> BuildQuestEvents(IReadOnlyList<GameCatalogRawQuestEvent> raw) =>
        raw
            .OrderBy(quest => quest.UnitId, StringComparer.Ordinal)
            .Select(quest => new GameCatalogQuestEventView(
                quest.UnitId,
                quest.AllowedFactions,
                quest.EnemyFactions,
                quest.Tiers
                    .OrderBy(tier => tier.Index)
                    .SelectMany(tier => tier.Battles.Select((battle, position) => BuildQuestBattle(tier.Index, position + 1, battle)))
                    .OrderBy(battle => battle.Number)
                    .ToArray()))
            .ToArray();

    public static IReadOnlyList<GameCatalogQuestCrate> BuildQuestCrates(IReadOnlyList<GameCatalogQuestCrate> raw) =>
        raw.OrderBy(crate => crate.Number).ToArray();

    internal static int QuestDeploySlotsFor(string tier, int position) =>
        QuestDeploySlots.TryGetValue(tier, out var slots) && position >= 1 && position <= slots.Length ? slots[position - 1] : 3;

    internal static string? QuestObjectiveTarget(JsonElement? target) => target switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        // The raw text, so a decimal or out-of-range number reaches validation instead of throwing here.
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        _ => null,
    };

    private static GameCatalogQuestBattleView BuildQuestBattle(int tierIndex, int position, GameCatalogRawQuestBattle battle)
    {
        // An out-of-range tier index is reported by validation; it maps to Common here so the build never throws.
        var tier = tierIndex >= 0 && tierIndex < QuestTierRarities.Length ? QuestTierRarities[tierIndex] : QuestTierRarities[0];
        _ = int.TryParse(battle.BattleNr, NumberStyles.None, CultureInfo.InvariantCulture, out var number);

        // Same NPC at the same index merges into one entry with a count, in order of first appearance (the lre-battles shape).
        var enemies = battle.Enemies
            .Select(enemy => (enemy.Name, LadderIndex: enemy.ProgressionIndex ?? 1))
            .GroupBy(enemy => enemy)
            .Select(group => new GameCatalogQuestEnemyView(group.Key.Name, group.Key.LadderIndex, group.Count()))
            .ToArray();

        return new GameCatalogQuestBattleView(
            number,
            tier,
            position,
            QuestDeploySlotsFor(tier, position),
            battle.BoardId,
            battle.SpawnPointsSet,
            battle.LightningVictory,
            enemies,
            battle.Objectives
                .Select(objective => new GameCatalogQuestObjectiveView(
                    objective.ObjectiveType, QuestObjectiveTarget(objective.ObjectiveTarget), objective.Score))
                .ToArray(),
            new GameCatalogQuestLootView(battle.Loot.Reward.UpgradeId, battle.Loot.Reward.Num, battle.Loot.Reward.Denom));
    }
}
