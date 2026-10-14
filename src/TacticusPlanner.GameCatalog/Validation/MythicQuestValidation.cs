using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static readonly int[] MythicQuestNumbers = [1, 2, 3, 4, 5];

    private static void ValidateMythicQuests(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors)
    {
        var characterIds = new HashSet<string>(snapshot.Characters.Select(character => character.Id), StringComparer.Ordinal);
        var equipmentIds = new HashSet<string>(snapshot.Equipment.Select(item => item.Id), StringComparer.Ordinal);
        ValidateMythicQuests(snapshot.MythicQuests, characterIds, equipmentIds, errors);
    }

    // Runs over the raw reward strings. A task's heroId parameter is deliberately not checked: V1 names summons
    // and NPCs there, and one of them (tauSmnDroneSniper) is not a catalog npc.
    internal static void ValidateMythicQuests(
        IReadOnlyList<GameCatalogRawMythicQuests> raw,
        HashSet<string> characterIds,
        HashSet<string> equipmentIds,
        List<GameCatalogValidationError> errors)
    {
        const string dataset = GameCatalogDatasets.MythicQuests;

        if (raw.Count == 0)
        {
            errors.Add(new GameCatalogValidationError(dataset, "EmptyDataset", $"Raw dataset '{dataset}' is empty."));
            return;
        }

        ValidateUniqueIds(dataset, raw, character => character.CharacterId, errors);

        foreach (var character in raw)
        {
            RequireReference(dataset, character.CharacterId, "characterId", character.CharacterId, characterIds, errors);

            if (!character.Quests.Select(quest => quest.Number).Order().SequenceEqual(MythicQuestNumbers))
            {
                errors.Add(new GameCatalogValidationError(
                    dataset, "InvalidQuestNumbers", $"'{character.CharacterId}' quest numbers are not exactly 1 to 5."));
            }

            foreach (var quest in character.Quests)
            {
                var owner = $"{character.CharacterId}/{quest.Number}";

                if (quest.Tasks.Count == 0)
                {
                    errors.Add(new GameCatalogValidationError(dataset, "MissingTasks", $"Quest '{owner}' has no tasks."));
                }

                if (quest.Tasks.Any(task => task.Target < 1))
                {
                    errors.Add(new GameCatalogValidationError(dataset, "InvalidTarget", $"Quest '{owner}' has a task target below 1."));
                }

                foreach (var reward in quest.Rewards)
                {
                    if (!MythicQuestRewardParser.TryParse(reward, out var view))
                    {
                        errors.Add(new GameCatalogValidationError(
                            dataset, "InvalidReward", $"Quest '{owner}' has an unrecognized reward '{reward}'."));
                        continue;
                    }

                    if (view.EquipmentId is { } equipmentId)
                    {
                        RequireReference(dataset, owner, "reward equipmentId", equipmentId, equipmentIds, errors);
                    }

                    if (view.UnitId is { } unitId)
                    {
                        RequireReference(dataset, owner, "reward unitId", unitId, characterIds, errors);
                    }
                }
            }
        }
    }
}
