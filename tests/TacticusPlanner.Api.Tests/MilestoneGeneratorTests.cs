using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.Tests;

/// <summary>Exercises <see cref="MilestoneGenerator"/> directly — pure breakpoint math, no HTTP host or
/// database involved.</summary>
public sealed class MilestoneGeneratorTests
{
    [Fact]
    public void SpansMultipleBreakpoints()
    {
        // Stone1 (0) -> Diamond1 (15): crosses Bronze1(6), Silver1(9), Gold1(12), then the target itself.
        var milestones = MilestoneGenerator.ForRank(0, 15);

        Assert.Equal(
            ["Bronze1", "Silver1", "Gold1", "Diamond1"],
            milestones.Select(m => m.TargetState)
        );
        Assert.Equal([0, 1, 2, 3], milestones.Select(m => m.Index));
        Assert.All(milestones, m => Assert.Equal("rank", m.Kind));
        Assert.All(milestones, m => Assert.Equal("calculated", m.Source));
        Assert.All(milestones, m => Assert.Equal("pending", m.Status));
        Assert.All(milestones, m => Assert.Null(m.CompletedAt));
    }

    [Fact]
    public void EndsAtTheLaddersLastRank()
    {
        // Diamond3 (17) -> Adamantine2 (19): the ladder's last breakpoint is the target itself, so it
        // isn't duplicated.
        var milestones = MilestoneGenerator.ForRank(17, 19);

        Assert.Equal(["Adamantine2"], milestones.Select(m => m.TargetState));
    }

    [Fact]
    public void AppendsTheTargetWhenItIsNotABreakpoint()
    {
        // Stone1 (0) -> Iron2 (4): no breakpoint falls in (0, 4], so the target itself is the only milestone.
        var milestones = MilestoneGenerator.ForRank(0, 4);

        Assert.Equal(["Iron2"], milestones.Select(m => m.TargetState));
    }

    [Fact]
    public void SingleRankStepStillYieldsOneMilestone()
    {
        var milestones = MilestoneGenerator.ForRank(3, 4);

        Assert.Equal(["Iron2"], milestones.Select(m => m.TargetState));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(10, 3)]
    public void IsEmptyForAnEmptyOrInvertedRange(int start, int end)
    {
        Assert.Empty(MilestoneGenerator.ForRank(start, end));
    }

    [Fact]
    public void EveryStepRankStrategyCreatesEveryTransition()
    {
        Assert.Equal(
            ["Stone2", "Stone3", "Iron1"],
            MilestoneGenerator.ForRank(0, 3, FarmingStrategy.EveryStep)
                .Select(milestone => milestone.TargetState));
    }

    [Fact]
    public void MowMajorMilestonesAppendTheRequestedFinalTarget()
    {
        Assert.Equal(
            ["35", "50", "55"],
            MilestoneGenerator.ForAbility(20, 55, FarmingStrategy.MajorMilestones)
                .Select(milestone => milestone.TargetState));
    }
}
