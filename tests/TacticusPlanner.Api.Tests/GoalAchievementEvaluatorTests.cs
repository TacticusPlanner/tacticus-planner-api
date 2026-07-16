using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;

namespace TacticusPlanner.Api.Tests;

public sealed class GoalAchievementEvaluatorTests
{
    [Fact]
    public void EvaluatesRankProgressionUnlockAndAbilityUsingConcretePlayerState()
    {
        var snapshot = new PlayerDataSnapshot
        {
            Characters =
            [
                new PlayerCharacterRecord
                {
                    UnitId = UnitId.From("char"),
                    Rank = UnitRank.Gold1,
                    ProgressionIndex = UnitProgression.EpicRedThreeStars,
                    AppliedUpgradeSlots = [0, 1, 2],
                    Abilities =
                    [
                        new PlayerUnitAbilityRecord { Level = 35 },
                        new PlayerUnitAbilityRecord { Level = 20 },
                    ],
                },
            ],
        };

        Assert.True(GoalAchievementEvaluator.IsAchieved(Goal(GoalType.Unlock), snapshot));
        Assert.True(GoalAchievementEvaluator.IsAchieved(Goal(GoalType.Rank, config: new GoalConfig
        {
            Rank = new RankTarget { Start = 11, End = 12, EndPointFive = true },
        }), snapshot));
        Assert.True(GoalAchievementEvaluator.IsAchieved(Goal(GoalType.Ascension, config: new GoalConfig
        {
            Progression = new ProgressionTarget { Start = "Epic:RedOneStar", End = "Epic:RedThreeStars" },
        }), snapshot));
        Assert.True(GoalAchievementEvaluator.IsAchieved(Goal(GoalType.Ability, config: new GoalConfig
        {
            Ability = new AbilityTarget { ActiveStart = 34, ActiveEnd = 35, PassiveStart = 20, PassiveEnd = 20 },
        }), snapshot));
    }

    [Fact]
    public void RankPointFiveRequiresThreeAppliedSlotsAtTheTargetRank()
    {
        var snapshot = new PlayerDataSnapshot
        {
            Characters =
            [
                new PlayerCharacterRecord
                {
                    UnitId = UnitId.From("char"),
                    Rank = UnitRank.Gold1,
                    AppliedUpgradeSlots = [0, 1],
                },
            ],
        };
        var goal = Goal(GoalType.Rank, config: new GoalConfig
        {
            Rank = new RankTarget { Start = 11, End = 12, EndPointFive = true },
        });

        Assert.False(GoalAchievementEvaluator.IsAchieved(goal, snapshot));
    }

    [Fact]
    public void IntermediateRankMilestoneCanCompleteBeforePointFiveFinalTarget()
    {
        var snapshot = new PlayerDataSnapshot
        {
            Characters =
            [
                new PlayerCharacterRecord
                {
                    UnitId = UnitId.From("char"),
                    Rank = UnitRank.Gold1,
                    AppliedUpgradeSlots = [0, 1],
                },
            ],
        };
        var goal = Goal(GoalType.Rank, config: new GoalConfig
        {
            Rank = new RankTarget { Start = (int)UnitRank.Silver1, End = (int)UnitRank.Gold1, EndPointFive = true },
        });
        goal.Milestones =
        [
            new GoalMilestone { Index = 0, Kind = "rank", TargetState = "Silver3", Source = "calculated", Status = "pending" },
            new GoalMilestone { Index = 1, Kind = "rank", TargetState = "Gold1", Source = "calculated", Status = "pending" },
        ];

        Assert.True(GoalAchievementEvaluator.IsMilestoneAchieved(goal, goal.Milestones[0], snapshot));
        Assert.False(GoalAchievementEvaluator.IsMilestoneAchieved(goal, goal.Milestones[1], snapshot));
    }

    [Fact]
    public void AbilityMilestoneUsesOnlyTheSelectedTrack()
    {
        var snapshot = new PlayerDataSnapshot
        {
            Mows =
            [
                new PlayerMowRecord
                {
                    UnitId = UnitId.From("mow"),
                    Abilities =
                    [
                        new PlayerUnitAbilityRecord { Level = 50 },
                        new PlayerUnitAbilityRecord { Level = 20 },
                    ],
                },
            ],
        };
        var goal = Goal(GoalType.Ability, "mow", new GoalConfig
        {
            Ability = new AbilityTarget { ActiveStart = 35, ActiveEnd = 50, PassiveStart = 20, PassiveEnd = 20 },
        });
        goal.EntityType = GoalEntityType.Mow;
        goal.Milestones =
        [
            new GoalMilestone { Index = 0, Kind = "ability", TargetState = "50", Source = "calculated", Status = "pending" },
        ];

        Assert.True(GoalAchievementEvaluator.IsMilestoneAchieved(goal, goal.Milestones[0], snapshot));
    }

    private static Goal Goal(GoalType type, string entityId = "char", GoalConfig? config = null) => new()
    {
        EntityId = entityId,
        EntityType = GoalEntityType.Character,
        GoalType = type,
        Config = config ?? new GoalConfig(),
    };
}
