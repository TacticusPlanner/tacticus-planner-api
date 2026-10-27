using System.Text.Json;

namespace TacticusPlanner.GameCatalog.Models;

public sealed record GameCatalogCampaignGroup(
    string GroupId,
    string Faction,
    string ReleaseType,
    // Allied side usable in this campaign group (the factions its campaigns allow); copied onto every served battle.
    string AlliesAlliance,
    IReadOnlyList<string> AlliesFactions,
    IReadOnlyList<string> CoreCharacters,
    // Distinct Tacticus campaign types present in this group's battles — a singleton for
    // storyline/mirror/elite/eliteMirror groups (which are split one-type-per-group), or
    // ["Standard", "Extremis"] for a campaign-event group (Tacticus reports event progress as one id
    // with multiple types, not separate ids — see GameCatalogDatasets.CampaignBattleGroups remarks).
    IReadOnlyList<string> Types,
    IReadOnlyList<GameCatalogCampaignBattle> Battles,
    // The campaign's number: 1-4 for a storyline (shared by its Standard, Mirror, Elite and EliteMirror groups), the
    // event's n for eventCampaign{n}.
    int Number
);

public sealed record GameCatalogCampaignBattle(
    string Id,
    // Tacticus's own campaign-type vocabulary (Standard/Mirror/Elite/EliteMirror/Extremis), not the
    // catalog's old ad-hoc difficulty strings.
    string Type,
    // True for the "Challenge" tier of a campaign-event battle — a finer split than Tacticus's own
    // `type` field, which doesn't distinguish challenge nodes. Always false outside campaign events.
    bool Challenge,
    int EnergyCost,
    int NodeNumber,
    int Slots,
    GameCatalogCampaignRewards Rewards,
    int EnemyPower,
    IReadOnlyList<string> EnemiesAlliances,
    IReadOnlyList<string> EnemiesFactions,
    int EnemiesTotal,
    IReadOnlyList<string> EnemiesTypes,
    IReadOnlyList<JsonElement> RawEnemyTypes,
    IReadOnlyList<GameCatalogCampaignDetailedEnemy> DetailedEnemyTypes,
    // The turn limit of the battle's lightning victory, or null when the battle has none (early Indomitus nodes).
    int? LightningVictoryTurns
)
{
    // Zero-based index used by Tacticus campaign-progress payloads. Event groups contain multiple
    // tracks, so this is assigned independently within each Type when the catalog is loaded.
    public int BattleIndex { get; init; } = -1;
}

public sealed record GameCatalogCampaignRewards(
    IReadOnlyList<GameCatalogCampaignGuaranteedReward> Guaranteed,
    IReadOnlyList<GameCatalogCampaignPotentialReward> Potential
)
{
    public IEnumerable<string> AllRewardIds =>
        Guaranteed.Select(reward => reward.Id).Concat(Potential.Select(reward => reward.Id));
}

public sealed record GameCatalogCampaignGuaranteedReward(
    string Id,
    int Min,
    int Max
);

public sealed record GameCatalogCampaignPotentialReward(
    string Id,
    string ChanceId
);

public sealed record GameCatalogDropChance(
    string Id,
    string RewardKind,
    string Difficulty,
    int Numerator,
    int Denominator,
    double EffectiveRate
);

public sealed record GameCatalogCampaignDetailedEnemy(
    string Id,
    string Name,
    int Count,
    int Stars,
    string Rank
);

public sealed record GameCatalogCampaignPotentialRewardView(
    string Id,
    string? ChanceId,
    string? RewardKind,
    int? Numerator,
    int? Denominator,
    double? EffectiveRate
);

public sealed record GameCatalogCampaignRewardsView(
    IReadOnlyList<GameCatalogCampaignGuaranteedReward> Guaranteed,
    IReadOnlyList<GameCatalogCampaignPotentialRewardView> Potential
);

public sealed record GameCatalogCampaignBattleView(
    string Id,
    string CampaignGroupId,
    string Type,
    bool Challenge,
    int EnergyCost,
    int NodeNumber,
    // Zero-based index used by Tacticus campaign-progress/battle-attempt payloads, assigned
    // independently within each {CampaignGroupId, Type} track — see GameCatalogCampaignBattle.BattleIndex.
    // Unlike NodeNumber, this distinguishes a challenge battle from the regular battle it shares a node
    // number with.
    int BattleIndex,
    int Slots,
    // The battle's daily attempt cap, derived from Type (not authored per-battle — see
    // GameCatalogDenormalizer.DailyAttemptsFor) since it's a fixed game rule per campaign
    // difficulty, except the early Indomitus nodes I06-I14, which cap at 3.
    int DailyAttempts,
    GameCatalogCampaignRewardsView Rewards,
    int EnemyPower,
    string AlliesAlliance,
    IReadOnlyList<string> AlliesFactions,
    IReadOnlyList<string> EnemiesAlliances,
    IReadOnlyList<string> EnemiesFactions,
    int EnemiesTotal,
    IReadOnlyList<string> EnemiesTypes,
    IReadOnlyList<GameCatalogCampaignDetailedEnemy> DetailedEnemyTypes,
    // The turn limit of a lightning victory (the game's lightningVictory), or null when the battle has none.
    int? LightningVictoryTurns
);

// One campaign group's definition: its metadata plus the ids of the battles that belong to it (the
// battle bodies live in the campaign-battles dataset, keyed by battle id).
public sealed record GameCatalogCampaignDefinitionView(
    string GroupId,
    string Faction,
    string ReleaseType,
    IReadOnlyList<string> CoreCharacters,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> BattleIds,
    // 1-4 for a storyline group (campaign1, mirror1, elite1 and eliteMirror1 are all 1), n for eventCampaign{n}.
    int Number
);

// ---- datamine shape (bound from Data/datamine/raw/gameconfig/battles/campaigns.json) ----------------------------
// campaigns.json is keyed by campaign kind (Standard, Mirror, Elite, EliteMirror, Event), each a list of campaigns.
// Only the fields the catalog reads are bound; boards, xp, tutorials, star chests and variations are dropped.

public sealed record DatamineCampaign(
    string Id,
    IReadOnlyList<DatamineCampaignBattle> Battles,
    DatamineCampaignUnlockConditions? UnlockConditions = null
);

// What a campaign needs before it opens: units from the allowed factions (some campaigns name the exact units to
// unlock) and, for an elite or extremis campaign, the campaign it follows.
public sealed record DatamineCampaignUnlockConditions(
    DatamineCampaignRequiredUnits? RequiredUnits,
    IReadOnlyList<DatamineCompletedCampaign>? CompletedCampaigns
);

public sealed record DatamineCampaignRequiredUnits(
    IReadOnlyList<string>? AllowedFactions,
    IReadOnlyList<string>? UnlockedUnitIds
);

public sealed record DatamineCompletedCampaign(string CampaignType, string CampaignId);

// `battleId` is the node number ("07"), with a "B" suffix for an event challenge battle ("13B"). `units` holds one
// list of "npcId:index" entries per team, `playerTeams` the matching team ids (1 fights with the player). A unit
// entry without an index is a board power-up, not an enemy. `lightningVictory` is the turn limit of a lightning
// victory (absent when the battle has none); `campaignId` is the storyline number (event campaigns share ids).
public sealed record DatamineCampaignBattle(
    string BattleId,
    int? StaminaCost,
    int Spawnpoints,
    int Power,
    IReadOnlyList<IReadOnlyList<string>> Units,
    IReadOnlyList<int> PlayerTeams,
    DatamineCampaignLoot Loot,
    int? LightningVictory = null,
    int? CampaignId = null
);

// `base` are "id[:min[-max]]" guaranteed rewards; `chanceOf` is "id%num/denom".
public sealed record DatamineCampaignLoot(IReadOnlyList<string>? Base, string? ChanceOf);

// ---- curated (Data/curated/campaign-curation.json) -----------------------------------------------------------------
// One entry per campaign-battles dataset key: the game campaigns the group reads, in order, with the prefix of their
// battle ids (V1's ids, which the game does not have). Everything else comes from the game's unlock conditions; core
// characters are curated only for a group whose campaigns name no units to unlock (Indomitus, the tutorial).
public sealed record GameCatalogCampaignCuration(
    IReadOnlyList<GameCatalogCampaignSourceCuration> Campaigns,
    IReadOnlyList<string>? CoreCharacters = null
);

public sealed record GameCatalogCampaignSourceCuration(string Campaign, string BattleIdPrefix);
