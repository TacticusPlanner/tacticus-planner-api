using System.Text.Json;

namespace TacticusPlanner.GameCatalog.Models;

public sealed record GameCatalogCampaignGroup(
    string GroupId,
    string Faction,
    string ReleaseType,
    IReadOnlyList<string> CoreCharacters,
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<GameCatalogCampaignBattle> Battles
);

public sealed record GameCatalogCampaignBattle(
    string Id,
    string Difficulty,
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
    string Difficulty,
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
    IReadOnlyList<string> Difficulties,
    IReadOnlyList<string> BattleIds
);
