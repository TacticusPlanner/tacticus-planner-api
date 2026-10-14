using System.Text.Json.Serialization;

namespace TacticusPlanner.GameCatalog.Models;

// ---- raw authored shape (bound from Data/mythic-quests.json) --------------------------------------

/// <summary>
/// One character's mythic quests, ported from V1's <c>mythic_quests/data/mythic-quests.json</c> <c>quests[]</c>
/// with V1's display-only fields (<c>characterName</c>, quest and task <c>name</c>) dropped. Reward entries stay
/// V1's <c>"type[:amount]"</c> strings so a refresh diffs cleanly against V1; they are parsed into structured
/// records by <c>Denormalization/MythicQuestDenormalizer.cs</c>.
/// </summary>
public sealed record GameCatalogRawMythicQuests(
    string CharacterId,
    IReadOnlyList<GameCatalogRawMythicQuest> Quests
);

public sealed record GameCatalogRawMythicQuest(
    int Number,
    IReadOnlyList<string> Rewards,
    IReadOnlyList<GameCatalogRawMythicQuestTask> Tasks
);

public sealed record GameCatalogRawMythicQuestTask(
    string LocaKey,
    int Target,
    IReadOnlyDictionary<string, string> TaskParameters
);

// ---- served shape ----------------------------------------------------------------------------------

public sealed record GameCatalogMythicQuestsView(
    string UnitId,
    IReadOnlyList<GameCatalogMythicQuestView> Quests
);

public sealed record GameCatalogMythicQuestView(
    int Number,
    IReadOnlyList<GameCatalogMythicQuestTaskView> Tasks,
    IReadOnlyList<GameCatalogMythicQuestRewardView> Rewards
);

// Parameters are V1's taskParameters unchanged (string values, e.g. killAmount "8"); the client fills the
// task-text template keyed by LocaKey from them.
public sealed record GameCatalogMythicQuestTaskView(
    string LocaKey,
    int Target,
    IReadOnlyDictionary<string, string> Parameters
);

/// <summary>
/// One parsed reward. <see cref="Type"/> is one of <c>MythicShards</c> (with <see cref="UnitId"/>),
/// <c>AscensionOrb</c> / <c>AbilityBadge</c> (with <see cref="Rarity"/> and <see cref="Alliance"/>),
/// <c>XpBook</c> (with <see cref="Rarity"/>), <c>Gold</c>, or <c>Equipment</c> (with
/// <see cref="EquipmentId"/>); fields a type does not use are omitted.
/// </summary>
public sealed record GameCatalogMythicQuestRewardView(
    string Type,
    int Amount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? UnitId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Rarity = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Alliance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EquipmentId = null
);
