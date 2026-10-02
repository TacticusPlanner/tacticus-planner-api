using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Tests;

public sealed class AcquisitionSourceRulesTests
{
    private static readonly HashSet<string> NoBattles = new(StringComparer.Ordinal);
    private static readonly HashSet<string> KnownShops =
        new(StringComparer.Ordinal) { "guild", "crusade", "rogue-trader", "war" };

    private static List<AcquisitionSourceRequest> Shop(params string[] ids) =>
        [new(AcquisitionSourceKinds.Shop, [.. ids])];

    private static string? Semantic(List<AcquisitionSourceRequest> sources, GoalType goalType, GoalEntityType entityType) =>
        AcquisitionSourceRules.SemanticError(sources, goalType, entityType, NoBattles, NoBattles, KnownShops);

    [Theory]
    [InlineData("guild:upgHpM004")]
    [InlineData("rogue-trader:upgHpM001")]
    [InlineData("guild:shards_ragnar")]
    [InlineData("guild:mythicShards_ragnar")]
    public void ShapeAcceptsShardAndMythicMaterialOffers(string id) =>
        Assert.Null(AcquisitionSourceRules.ShapeError(Shop(id)));

    [Theory]
    [InlineData("guild:upgDmgL202")]
    [InlineData("guild:upgHpM005")]
    [InlineData("upgHpM004")]
    public void ShapeRejectsOtherRewardTypes(string id) =>
        Assert.NotNull(AcquisitionSourceRules.ShapeError(Shop(id)));

    [Theory]
    [InlineData(GoalType.Rank, GoalEntityType.Character)]
    [InlineData(GoalType.Upgrade, GoalEntityType.Character)]
    [InlineData(GoalType.Upgrade, GoalEntityType.Mow)]
    [InlineData(GoalType.Ability, GoalEntityType.Mow)]
    public void MythicMaterialOffersAreAcceptedOnMaterialGoals(GoalType goalType, GoalEntityType entityType)
    {
        Assert.Null(Semantic(Shop("guild:upgHpM004", "crusade:upgHpM004"), goalType, entityType));
        Assert.Null(Semantic(Shop(), goalType, entityType));
    }

    [Fact]
    public void ShardOfferIsRejectedOnRankGoal() =>
        Assert.NotNull(Semantic(Shop("guild:shards_ragnar"), GoalType.Rank, GoalEntityType.Character));

    [Fact]
    public void MythicMaterialOfferIsRejectedOnAscensionGoal() =>
        Assert.NotNull(Semantic(Shop("guild:upgHpM004"), GoalType.Ascension, GoalEntityType.Character));

    [Fact]
    public void UnknownShopIsRejectedOnRankGoal() =>
        Assert.NotNull(Semantic(Shop("auction:upgHpM004"), GoalType.Rank, GoalEntityType.Character));

    [Theory]
    [InlineData(AcquisitionSourceKinds.Campaign)]
    [InlineData(AcquisitionSourceKinds.Onslaught)]
    public void NonShopKindsAreRejectedOnRankGoal(string kind) =>
        Assert.NotNull(Semantic([new(kind, [])], GoalType.Rank, GoalEntityType.Character));

    [Fact]
    public void ShopIsRejectedOnCharacterAbilityGoal() =>
        Assert.NotNull(Semantic(Shop("guild:upgHpM004"), GoalType.Ability, GoalEntityType.Character));

    [Fact]
    public void ShopIsRejectedOnMowUnlockGoal() =>
        Assert.NotNull(Semantic(Shop("guild:upgHpM004"), GoalType.Unlock, GoalEntityType.Mow));

    [Fact]
    public void ShardOfferStillAcceptedOnCharacterUnlockGoal() =>
        Assert.Null(Semantic(Shop("guild:shards_ragnar"), GoalType.Unlock, GoalEntityType.Character));
}
