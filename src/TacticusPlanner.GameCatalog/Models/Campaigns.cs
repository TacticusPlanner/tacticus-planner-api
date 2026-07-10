using System.Text.Json;

namespace TacticusPlanner.GameCatalog.Models;

public sealed record GameCatalogCampaignGroup(
    string GroupId,
    string Faction,
    string ReleaseType,
    IReadOnlyList<string> CoreCharacters,
    // Distinct Tacticus campaign types present in this group's battles — a singleton for
    // storyline/mirror/elite/eliteMirror groups (which are split one-type-per-group), or
    // ["Standard", "Extremis"] for a campaign-event group (Tacticus reports event progress as one id
    // with multiple types, not separate ids — see GameCatalogDatasets.CampaignBattleGroups remarks).
    IReadOnlyList<string> Types,
    IReadOnlyList<GameCatalogCampaignBattle> Battles
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
    IReadOnlyList<GameCatalogCampaignDetailedEnemy> DetailedEnemyTypes
);

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
    int Slots,
    GameCatalogCampaignRewardsView Rewards,
    int EnemyPower,
    IReadOnlyList<string> EnemiesAlliances,
    IReadOnlyList<string> EnemiesFactions,
    int EnemiesTotal,
    IReadOnlyList<string> EnemiesTypes,
    IReadOnlyList<GameCatalogCampaignDetailedEnemy> DetailedEnemyTypes
);

// One campaign group's definition: its metadata plus the ids of the battles that belong to it (the
// battle bodies live in the campaign-battles dataset, keyed by battle id).
public sealed record GameCatalogCampaignDefinitionView(
    string GroupId,
    string Faction,
    string ReleaseType,
    IReadOnlyList<string> CoreCharacters,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> BattleIds
);
