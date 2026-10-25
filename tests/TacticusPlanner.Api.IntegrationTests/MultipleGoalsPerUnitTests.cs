using System.Net;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>
/// Covers <c>support-multiple-goals-per-unit</c>: every goal type except Unlock holds one in-flight slot per
/// distinct end target (<see cref="GoalTargetKey"/>), so a unit can carry a milestone ladder while exact
/// duplicates are still refused — in combined creation, target edits and status transitions alike.
/// </summary>
public sealed class MultipleGoalsPerUnitTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string CharacterId = "blackTerminator";

    [Fact]
    public async Task TwoAscensionSpecsWithDistinctEndsAreCreatedInOneRequest()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(Combined(
            Ascension(UnitProgression.CommonTwoStars),
            Ascension(UnitProgression.UncommonTwoStars) with { DependsOnIndex = [0] }));

        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken))!;
        Assert.Equal(2, created.Goals.Count);
        Assert.Equal([created.Goals[0].GoalId], created.Goals[1].DependsOn);
    }

    [Fact]
    public async Task TwoAscensionSpecsWithTheSameEndAreASlotConflict()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(Combined(
            Ascension(UnitProgression.CommonTwoStars),
            new CombinedGoalSpec(GoalType.Ascension, new CreateGoalConfigRequest(
                Progression: new ProgressionTargetRequest(UnitProgression.CommonOneStar, UnitProgression.CommonTwoStars)), [])));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("slotOccupied", await response.ReadIssueCodeAsync());
        Assert.Empty((await client.ListGoalsAsync()).Goals);
    }

    [Fact]
    public async Task ARankLadderIsCreatedAtomicallyWithConsecutivePrioritiesAndAChain()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(Combined(
            Rank((int)UnitRank.Gold1),
            Rank((int)UnitRank.Diamond1) with { DependsOnIndex = [0] },
            Rank((int)UnitRank.Diamond3) with { DependsOnIndex = [1] },
            Rank((int)UnitRank.Adamantine1) with { DependsOnIndex = [2] }));

        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken))!;
        Assert.Equal(4, created.Goals.Count);
        for (var i = 1; i < created.Goals.Count; i++)
        {
            Assert.Equal([created.Goals[i - 1].GoalId], created.Goals[i].DependsOn);
            Assert.Equal(created.Goals[i - 1].GlobalPriority + 1, created.Goals[i].GlobalPriority);
        }
    }

    [Fact]
    public async Task UnlockStaysOnePerUnit()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var inOneRequest = await client.PostGoalAsync(Combined(Unlock(), Unlock()));
        Assert.Equal(HttpStatusCode.Conflict, inOneRequest.StatusCode);

        (await client.PostGoalAsync(Combined(Unlock()))).EnsureSuccessStatusCode();
        var second = await client.PostGoalAsync(Combined(Unlock()));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("slotOccupied", await second.ReadIssueCodeAsync());
    }

    [Fact]
    public async Task AbilityAndUpgradeGoalsWithDistinctTargetsCoexist()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        (await client.PostGoalAsync(Combined(Ability(10, 0)))).EnsureSuccessStatusCode();
        (await client.PostGoalAsync(Combined(Ability(20, 0)))).EnsureSuccessStatusCode();
        var duplicate = await client.PostGoalAsync(Combined(Ability(20, 0)));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.ReadProblemAsync();
        Assert.Equal("20:-", problem["normalizedTarget"]!.GetValue<string>());
    }

    [Fact]
    public async Task EditingATargetOntoAnotherGoalsTargetIsASlotConflict()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var low = await client.CreateGoalAsync(Single(Ascension(UnitProgression.CommonTwoStars)));
        await client.CreateGoalAsync(Single(Ascension(UnitProgression.UncommonTwoStars)));

        var colliding = await client.PutTargetAsync(low.GoalId, ProgressionEdit(low.Revision, UnitProgression.UncommonTwoStars));
        Assert.Equal(HttpStatusCode.Conflict, colliding.StatusCode);
        Assert.Equal("slotOccupied", await colliding.ReadIssueCodeAsync());

        var free = await client.PutTargetAsync(low.GoalId, ProgressionEdit(low.Revision, UnitProgression.UncommonThreeStars));
        Assert.Equal(UnitProgression.UncommonThreeStars, (await free.ReadEditedGoalAsync()).Config.Progression!.End);
    }

    [Fact]
    public async Task ResumingACompletedGoalChecksItsOwnTarget()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var low = await client.CreateGoalAsync(Single(Ascension(UnitProgression.CommonTwoStars)));
        (await client.PostStatusAsync(low.GoalId, GoalStatus.Completed)).EnsureSuccessStatusCode();
        await client.CreateGoalAsync(Single(Ascension(UnitProgression.UncommonTwoStars)));

        // A distinct end is in flight: resuming succeeds.
        (await client.PostStatusAsync(low.GoalId, GoalStatus.Active)).EnsureSuccessStatusCode();

        (await client.PostStatusAsync(low.GoalId, GoalStatus.Completed)).EnsureSuccessStatusCode();
        await client.CreateGoalAsync(Single(Ascension(UnitProgression.CommonTwoStars)));

        // The same end is in flight again: resuming conflicts.
        var resume = await client.PostStatusAsync(low.GoalId, GoalStatus.Active);
        Assert.Equal(HttpStatusCode.Conflict, resume.StatusCode);
        Assert.Equal("slotOccupied", await resume.ReadIssueCodeAsync());
    }

    private static CreateCombinedGoalsRequest Combined(params CombinedGoalSpec[] specs) =>
        new(GoalEntityType.Character, CharacterId, null, [.. specs]);

    private static CreateGoalRequest Single(CombinedGoalSpec spec) =>
        new(GoalEntityType.Character, CharacterId, spec.GoalType, spec.Config, null);

    private static CombinedGoalSpec Unlock() => new(GoalType.Unlock, new CreateGoalConfigRequest(), []);

    private static CombinedGoalSpec Ascension(UnitProgression end) =>
        new(GoalType.Ascension, new CreateGoalConfigRequest(
            Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, end)), []);

    private static CombinedGoalSpec Rank(int end) =>
        new(GoalType.Rank, new CreateGoalConfigRequest(Rank: new RankTargetRequest(0, false, 0, end, false, 0)), []);

    private static CombinedGoalSpec Ability(int activeEnd, int passiveEnd) =>
        new(GoalType.Ability, new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, activeEnd, 0, passiveEnd)), []);

    private static UpdateGoalTargetRequest ProgressionEdit(long revision, UnitProgression end) =>
        new(revision, new GoalTargetEditRequest(Progression: new ProgressionEndTargetRequest(end)));
}
