using System.Text.Json;

namespace TacticusPlanner.GameCatalog.Models;

// ---- datamine shapes for Legendary Release Events (bound from Data/datamine/raw/gameconfig/) --------------------
// Only the fields the catalog reads are bound; loca keys, visuals, offers, stamina and conversion fields are dropped.

// liveEvents/legendaryHeroEvent.json: one entry per event. `lanes` name the battle set of each track (lane 1 is
// alpha), `questGroups` the regular and premium mission chains, `rewardProgressId` the points ladder and
// `shopSegmentId` the chest shop.
public sealed record DatamineLegendaryHeroEvent(
    string EventName,
    string RewardProgressId,
    string ShopSegmentId,
    IReadOnlyList<string> QuestGroups,
    IReadOnlyList<DatamineLegendaryHeroLane> Lanes,
    DatamineLegendaryHeroUnlock UnlockableHero
);

public sealed record DatamineLegendaryHeroLane(int LaneIndex, string BattleSetId, string FactionId);

public sealed record DatamineLegendaryHeroUnlock(string HeroId);

// One battle of a battles/battleSets.json set. `army` lists one "npcId:progressionIndex" string per spawned enemy;
// `objectives` holds the Acing (defeat all) score followed by the track's restrictions.
public sealed record DatamineLreBattle(
    int Tier,
    int BattleNr,
    string BoardId,
    int Power,
    IReadOnlyList<DatamineLreWave> Waves,
    IReadOnlyList<DatamineLreObjective> Objectives,
    IReadOnlyList<string>? DisallowedFactions
);

public sealed record DatamineLreWave(int Round, int Power, IReadOnlyList<string> Army);

public sealed record DatamineLreObjective(string ObjectiveType, string? ObjectiveTarget, int Score);

// A quests/groups.json chain; `questType` is `standard` (regular missions) or `premium`.
public sealed record DatamineLreQuestGroup(string QuestType, IReadOnlyList<DatamineLreQuest> Quests);

public sealed record DatamineLreQuest(string Name, IReadOnlyList<DatamineLreQuestTask> Tasks);

public sealed record DatamineLreQuestTask(string Name, int Target, IReadOnlyDictionary<string, string>? TaskParameters);

// shop/segments.json: the products of a shop segment (the LRE chest ladder in order).
public sealed record DatamineShopSegment(string SegmentId, IReadOnlyList<DatamineShopProductSet> ProductSets);

public sealed record DatamineShopProductSet(IReadOnlyList<string> Products);

// shop/products.json: a chest product's cost and the loot/chests.json chest it opens.
public sealed record DatamineChestProduct(string ChestId, DatamineProductCost Cost);

public sealed record DatamineProductCost(string Type, int Amount);

// loot/chests.json: the reward strings of one chest variant.
public sealed record DatamineChest(IReadOnlyList<string> Rewards);

// units/heroProgressionStepsPerUnit.json: a unit's progression steps; `ascensionCosts` holds the event shards an
// LRE unit needs to reach the step.
public sealed record DatamineUnitProgressionStep(
    int Stars,
    string Rarity,
    IReadOnlyDictionary<string, int>? AscensionCosts
);

// The datamine files the lres datasets are read from. Battle sets, products, chests and quest groups hold entries
// of other shapes too, so they are bound as raw elements and only the entries an event names are read.
public sealed record DatamineLreSources(
    IReadOnlyList<DatamineLegendaryHeroEvent> Events,
    IReadOnlyDictionary<string, JsonElement> BattleSets,
    IReadOnlyDictionary<string, JsonElement> QuestGroups,
    IReadOnlyDictionary<string, List<DatamineTieredProgressReward>> Ladders,
    IReadOnlyList<DatamineShopSegment> ShopSegments,
    IReadOnlyDictionary<string, JsonElement> Products,
    IReadOnlyDictionary<string, JsonElement> Chests,
    IReadOnlyDictionary<string, List<DatamineUnitProgressionStep>> ProgressionSteps
);

// ---- curated (Data/curated/lre-curation.json) --------------------------------------------------------------------
// The one thing the game has no source for, keyed by the event unit: the announced run starts. Which units have an
// entry decides which events are served.
public sealed record GameCatalogLreCuration(IReadOnlyList<string>? RunStartDatesUtc);
