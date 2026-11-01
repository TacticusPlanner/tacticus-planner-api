namespace TacticusPlanner.GameCatalog.Models;

// One served Incursion (the incursions dataset): the game's Machine of War event for one MoW, read from the datamine
// `liveEvents/mowEvent.json` with its chests from `loot/chests.json`; see Denormalization/IncursionDenormalizer.cs.
// AvailableFromUtc is when the MoW's Incursion first ran (yyyy-MM-ddTHH:mm:ssZ); New / Updated are the game's flags
// for a debut and a revamp. AllowedFactions are the hero factions a run may bring. Rewards everywhere are the game's
// "type[:amount]" strings (amount "N" or "N-M"; absent means 1).
public sealed record GameCatalogIncursion(
    string MowId,
    string AvailableFromUtc,
    bool New,
    bool Updated,
    IReadOnlyList<string> AllowedFactions,
    GameCatalogIncursionTokens Tokens,
    int MinUnitsOfGrandAllianceNeeded,
    GameCatalogOhSoCloseOffer OhSoCloseOffer,
    IReadOnlyDictionary<string, GameCatalogIncursionEnhancement> Enhancements,
    IReadOnlyList<GameCatalogIncursionTier> Tiers
);

// The event's run tokens (the game's stamina): it starts at Start, holds at most Max and regains RegenAmount every
// RegenSeconds while below Max; starting a run costs CostPerRun.
public sealed record GameCatalogIncursionTokens(int Start, int Max, int RegenSeconds, int RegenAmount, int CostPerRun);

// A pickable run enhancement: the game's Type (Heal, Summons, AddTrait…), the ability it grants (null for Heal and
// Resurrect), how many units it applies to (null when the game names none) and whether picking it again stacks.
public sealed record GameCatalogIncursionEnhancement(string Type, string? AbilityId, int? UnitAmount, bool Stacks);

// One difficulty tier of the MoW's Incursion (0-based Index, in the game's order), its battles and its run map.
public sealed record GameCatalogIncursionTier(
    int Index,
    string Rarity,
    IReadOnlyList<GameCatalogIncursionBattle> Battles,
    IReadOnlyList<GameCatalogIncursionNode> Nodes
);

// One battle of a tier. Challenge marks a battle its node calls a challenge. StarRewards are the 1-, 2- and 3-star
// rewards in order (each is earned once); BaseRewards come with every win. Enemies are grouped by npc and level, in
// the game's spawn order of first appearance.
public sealed record GameCatalogIncursionBattle(
    string Id,
    string BoardId,
    bool Challenge,
    int DifficultyMultiplier,
    int? LightningVictoryTurns,
    IReadOnlyList<string> BaseRewards,
    IReadOnlyList<string> StarRewards,
    IReadOnlyList<GameCatalogIncursionEnemy> Enemies
);

public sealed record GameCatalogIncursionEnemy(string NpcId, int Level, int Count);

// One node of a tier's run map, a DAG walked from Depth 0. Kind is battle, enhancement or chest; exactly the matching
// BattleId / EnhancementId / Chest is set. A node opens once any node in UnlockedBy is done (empty for the start);
// picking it locks the nodes in ExclusiveWith. Name is the game's node label ("1", "E2", "C").
public sealed record GameCatalogIncursionNode(
    int Index,
    int Depth,
    string Kind,
    string Name,
    string? BattleId,
    string? EnhancementId,
    GameCatalogIncursionChest? Chest,
    IReadOnlyList<int> UnlockedBy,
    IReadOnlyList<int> ExclusiveWith
);

// A run-map chest: the game's chest id, its type (Bronze, Silver, Gold, Diamond) and its fixed rewards.
public sealed record GameCatalogIncursionChest(string Id, string Type, IReadOnlyList<string> Rewards);

// ---- datamine shapes -------------------------------------------------------------------------------------

// The single entry of Data/datamine/raw/gameconfig/liveEvents/mowEvent.json (display and tracking fields not bound).
public sealed record DatamineMowEvent(
    int StaminaConsumptionOnStart,
    int StartStamina,
    int MaxStamina,
    int StaminaRegenerationTime,
    int StaminaRegenerationAmount,
    int MinUnitsOfGrandAllianceNeeded,
    IReadOnlyList<DatamineMowEventMow>? Mows,
    GameCatalogOhSoCloseOffer? OhSoCloseOffer,
    IReadOnlyDictionary<string, DatamineMowEventEnhancement>? Enhancements
);

public sealed record DatamineMowEventMow(
    string UnitId,
    long AvailableFrom,
    bool New,
    bool Updated,
    IReadOnlyList<string>? AllowedFactions,
    IReadOnlyList<DatamineMowEventTier>? Tiers
);

public sealed record DatamineMowEventTier(
    int Index,
    string Rarity,
    IReadOnlyList<DatamineMowEventBattle>? Battles,
    IReadOnlyList<DatamineMowEventNode>? Nodes
);

public sealed record DatamineMowEventBattle(
    string BattleId,
    int DifficultyMultiplier,
    string BoardId,
    int? LightningVictory,
    DatamineMowEventLoot? Loot,
    IReadOnlyList<string>? Enemies
);

public sealed record DatamineMowEventLoot(IReadOnlyList<string>? Base, string? Star1, string? Star2, string? Star3);

public sealed record DatamineMowEventNode(
    int NodeDepth,
    string Type,
    string NodeName,
    int Index,
    string? BattleType,
    string? BattleId,
    string? EnhancementId,
    string? ChestId,
    IReadOnlyList<DatamineMowEventNodeRef>? UnlockedBy,
    IReadOnlyList<int>? ExclusiveWith
);

public sealed record DatamineMowEventNodeRef(int Index);

public sealed record DatamineMowEventEnhancement(string Type, int? UnitAmount, string? AbilityId, bool Stacks);
