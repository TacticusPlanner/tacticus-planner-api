using System.Globalization;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    // Battles per tier index 0 (Common) to 5 (Mythic).
    private static readonly int[] QuestTierBattleCounts = [3, 3, 5, 3, 2, 1];
    private const int QuestBattleCount = 17;
    private const int QuestCrateCount = 17;
    private const int QuestMedalsAndLightningScore = 300 + 100;

    private static readonly HashSet<string> QuestObjectiveTypesWithoutTarget = new(StringComparer.Ordinal)
    {
        "NoDamageTakenByAnyHero", "HasRangedAttack", "UseNoSummons", "DoNoHealOrRepair",
    };

    private static readonly HashSet<string> QuestObjectiveTypesWithTarget = new(StringComparer.Ordinal)
    {
        "NotTrait", "NotFaction", "NotDamageType", "DamageType", "MaxHits", "MinHits",
    };

    private static readonly HashSet<string> QuestCrateRewardTypes = new(StringComparer.Ordinal)
    {
        "Coins", "RaidTicket", "XpBook", "AbilityBadge", "AscensionOrb",
    };

    private static void ValidateQuestEvents(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        var characterIds = new HashSet<string>(snapshot.Characters.Select(character => character.Id), StringComparer.Ordinal);
        var factionIds = new HashSet<string>(
            snapshot.UnitsByFaction.Values.Select(faction => faction.FactionId)
                .Concat(snapshot.NpcsByFaction.Values.Select(faction => faction.FactionId)),
            StringComparer.Ordinal);
        var npcIds = new HashSet<string>(snapshot.Npcs.Select(npc => npc.Id), StringComparer.Ordinal);
        var upgradeIds = new HashSet<string>(snapshot.Upgrades.Select(upgrade => upgrade.Id), StringComparer.Ordinal);
        ValidateQuestEvents(snapshot.QuestEvents, snapshot.QuestCrates, characterIds, factionIds, npcIds, upgradeIds, errors);
    }

    // Runs over the raw files. ladderIndex is deliberately not checked against the NPC's ladder, as for lre-battles.
    internal static void ValidateQuestEvents(
        IReadOnlyList<GameCatalogRawQuestEvent> quests,
        IReadOnlyList<GameCatalogQuestCrate> crates,
        HashSet<string> characterIds,
        HashSet<string> factionIds,
        HashSet<string> npcIds,
        HashSet<string> upgradeIds,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.QuestEvents;
        const string cratesDataset = GameCatalogDatasets.QuestCrates;

        if (quests.Count == 0)
        {
            errors.Add(new GameCatalogValidationError(dataset, "EmptyDataset", $"Raw dataset '{dataset}' is empty."));
        }

        if (crates.Count == 0)
        {
            errors.Add(new GameCatalogValidationError(cratesDataset, "EmptyDataset", $"Raw dataset '{cratesDataset}' is empty."));
            return;
        }

        ValidateQuestCrates(crates, errors);
        var maxScore = crates.MaxBy(crate => crate.Number)!.Points;

        ValidateUniqueIds(dataset, quests, quest => quest.UnitId, errors);

        foreach (var quest in quests)
        {
            var id = quest.UnitId;
            Require(dataset, "(record)", id, "unitId", errors);
            RequireReference(dataset, id, "unitId", id, characterIds, errors);
            foreach (var faction in quest.AllowedFactions.Concat(quest.EnemyFactions))
            {
                RequireReference(dataset, id, "faction", faction, factionIds, errors);
            }

            var tiers = quest.Tiers.OrderBy(tier => tier.Index).ToArray();
            var battleNumbers = tiers.SelectMany(tier => tier.Battles).Select(battle => battle.BattleNr).ToArray();
            if (!tiers.Select(tier => tier.Index).SequenceEqual(Enumerable.Range(0, QuestTierBattleCounts.Length))
                || !tiers.Select(tier => tier.Battles.Count).SequenceEqual(QuestTierBattleCounts)
                || !battleNumbers.SequenceEqual(Enumerable.Range(1, QuestBattleCount).Select(n => n.ToString(CultureInfo.InvariantCulture))))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "InvalidQuestBattles",
                    $"Quest '{id}' must have {QuestBattleCount} battles numbered 1 to {QuestBattleCount}, split {string.Join("/", QuestTierBattleCounts)} across tiers 0 to 5."));
            }

            var score = 0;
            foreach (var battle in tiers.SelectMany(tier => tier.Battles))
            {
                var owner = $"{id}/{battle.BattleNr}";
                score += QuestMedalsAndLightningScore + battle.Objectives.Sum(objective => objective.Score);

                foreach (var enemy in battle.Enemies)
                {
                    RequireReference(dataset, owner, "enemy", enemy.Name, npcIds, errors);
                }

                RequireReference(dataset, owner, "loot upgradeId", battle.Loot.Reward.UpgradeId, upgradeIds, errors);
                if (battle.Loot.Reward.Num <= 0 || battle.Loot.Reward.Num > battle.Loot.Reward.Denom)
                {
                    errors.Add(new GameCatalogValidationError(dataset, "InvalidLootChance", $"Battle '{owner}' has an invalid loot chance."));
                }

                if (battle.Power is < 1)
                {
                    errors.Add(new GameCatalogValidationError(dataset, "InvalidPower", $"Battle '{owner}' has a power below 1."));
                }

                foreach (var objective in battle.Objectives)
                {
                    ValidateQuestObjective(dataset, owner, objective, errors);
                }
            }

            if (score != maxScore)
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "InvalidQuestScore", $"Quest '{id}' totals {score} points; the last crate needs {maxScore}."));
            }
        }
    }

    private static void ValidateQuestObjective(
        string dataset, string owner, GameCatalogRawQuestObjective objective, List<GameCatalogValidationError> errors)
    {
        var type = objective.ObjectiveType;
        var target = GameCatalogDenormalizer.QuestObjectiveTarget(objective.ObjectiveTarget);
        if (QuestObjectiveTypesWithoutTarget.Contains(type))
        {
            return;
        }

        if (!QuestObjectiveTypesWithTarget.Contains(type))
        {
            errors.Add(new GameCatalogValidationError(dataset, "UnknownObjective", $"Battle '{owner}' has an unknown objective '{type}'."));
            return;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            errors.Add(new GameCatalogValidationError(dataset, "MissingObjectiveTarget", $"Battle '{owner}' objective '{type}' has no target."));
        }
        else if (type is "MaxHits" or "MinHits"
            && (!int.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out var hits) || hits < 1))
        {
            errors.Add(new GameCatalogValidationError(
                dataset, "InvalidObjectiveTarget", $"Battle '{owner}' objective '{type}' needs a whole number of at least 1, not '{target}'."));
        }
    }

    private static void ValidateQuestCrates(IReadOnlyList<GameCatalogQuestCrate> crates, List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.QuestCrates;
        var ordered = crates.OrderBy(crate => crate.Number).ToArray();
        if (!ordered.Select(crate => crate.Number).SequenceEqual(Enumerable.Range(1, QuestCrateCount))
            || ordered.Zip(ordered.Skip(1)).Any(pair => pair.Second.Points <= pair.First.Points))
        {
            errors.Add(new GameCatalogValidationError(
                dataset, "InvalidCrates", $"'{dataset}' must be {QuestCrateCount} crates numbered 1 to {QuestCrateCount} with ascending points."));
        }

        foreach (var crate in ordered)
        {
            foreach (var reward in crate.Rewards)
            {
                if (!QuestCrateRewardTypes.Contains(reward.Type) || reward.Min < 1 || reward.Max < reward.Min)
                {
                    errors.Add(new GameCatalogValidationError(
                        dataset, "InvalidCrateReward", $"Crate {crate.Number} has an invalid reward '{reward.Type}' ({reward.Min}-{reward.Max})."));
                }
            }
        }
    }
}
