using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>The per-profile caps (non-archived goals per type, projects) and the bounds on client-supplied goal
/// lists.</summary>
public sealed class GoalCardinalityLimitTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string CharacterId = "blackTerminator";

    private readonly GameCatalogSnapshot catalog = factory.Services.GetRequiredService<IGameCatalogProvider>().Current;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CombinedGoalSpec Rank(int end) =>
        new(GoalType.Rank, new CreateGoalConfigRequest(Rank: new RankTargetRequest(0, false, 0, end, false, 0)), []);

    private static CreateCombinedGoalsRequest Request(params CombinedGoalSpec[] specs) =>
        new(GoalEntityType.Character, CharacterId, null, [.. specs]);

    private async Task<(HttpClient Client, string Subject)> ClientAsync()
    {
        var subject = $"limits-{Guid.NewGuid()}";
        return (await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject), subject);
    }

    private static async Task AssertGoalLimitAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal(GoalProblems.GoalLimitReached, problem["issueCode"]?.GetValue<string>());
        Assert.Contains($"{GoalValidation.MaxGoalsPerType}", problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>A target-validation 400, whose message lands under the general errors.</summary>
    private static async Task AssertTargetRejectedAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    // ----- Per-type goal cap -----

    [Fact]
    public async Task ABatchThatCrossesTheCapIsRejectedWholeAndOneThatReachesItIsAccepted()
    {
        var (client, subject) = await ClientAsync();
        await GoalsTestHelpers.SeedRankGoalsAsync(factory, subject, GoalValidation.MaxGoalsPerType - 1, GoalStatus.Completed);
        // Archived goals do not count.
        await GoalsTestHelpers.SeedRankGoalsAsync(factory, subject, 5, GoalStatus.Archived);

        await AssertGoalLimitAsync(await client.PostGoalAsync(Request(Rank(10), Rank(15))));
        Assert.DoesNotContain((await client.ListGoalsAsync()).Goals, goal => goal.Status == GoalStatus.Active);

        (await client.PostGoalAsync(Request(Rank(15)))).EnsureSuccessStatusCode();
        await AssertGoalLimitAsync(await client.PostGoalAsync(Request(Rank(10))));
    }

    [Fact]
    public async Task AMixedBatchIsRejectedWhenOneOfItsTypesIsFull()
    {
        var (client, subject) = await ClientAsync();
        await GoalsTestHelpers.SeedRankGoalsAsync(factory, subject, GoalValidation.MaxGoalsPerType, GoalStatus.Completed);
        var unlockThenRank = Request(
            new CombinedGoalSpec(GoalType.Unlock, new CreateGoalConfigRequest(), []),
            Rank(15) with { DependsOnIndex = [0] });

        await AssertGoalLimitAsync(await client.PostGoalAsync(unlockThenRank));

        Assert.DoesNotContain((await client.ListGoalsAsync()).Goals, goal => goal.GoalType == GoalType.Unlock);
        (await client.PostGoalAsync(Request(new CombinedGoalSpec(GoalType.Unlock, new CreateGoalConfigRequest(), [])))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task UnarchivingIsRejectedAtTheCap()
    {
        var (client, subject) = await ClientAsync();
        var completed = await GoalsTestHelpers.SeedRankGoalsAsync(factory, subject, GoalValidation.MaxGoalsPerType, GoalStatus.Completed);
        var archived = (await GoalsTestHelpers.SeedRankGoalsAsync(factory, subject, 1, GoalStatus.Archived)).Single();

        var rejected = await client.PostStatusAsync(GoalStatus.Completed, null, archived);
        await AssertGoalLimitAsync(rejected);
        Assert.Contains("goalIds", (await (await client.PostStatusAsync(GoalStatus.Active, null, archived)).ReadProblemAsync())["errors"]!.AsObject().Select(error => error.Key));

        (await client.PostStatusAsync(GoalStatus.Archived, null, completed[0])).EnsureSuccessStatusCode();
        (await client.PostStatusAsync(GoalStatus.Completed, null, archived)).EnsureSuccessStatusCode();
    }

    // ----- Snapshot resource bounds -----

    private static CreateCombinedGoalsRequest WithSnapshot(List<GoalSnapshotResourceRequest> requirement) =>
        Request(Rank(15) with { Snapshot = new CreateGoalSnapshotRequest(InitialRequirement: requirement) });

    private List<GoalSnapshotResourceRequest> Resources(int count) =>
        catalog.UpgradeViews.Take(count).Select(upgrade => new GoalSnapshotResourceRequest(upgrade.Id, 1)).ToList();

    [Fact]
    public async Task SnapshotListsAreBoundedInCount()
    {
        var (client, _) = await ClientAsync();

        await (await client.PostGoalAsync(WithSnapshot(Resources(GoalValidation.MaxResourcesPerGoal + 1)))).AssertFieldRejectedAsync("snapshot");
        (await client.PostGoalAsync(WithSnapshot(Resources(GoalValidation.MaxResourcesPerGoal)))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SnapshotResourceIdsMustBeDistinctShortAndInTheCatalog()
    {
        var (client, _) = await ClientAsync();
        var known = catalog.UpgradeViews[0].Id;

        await (await client.PostGoalAsync(WithSnapshot([new(known, 1), new($" {known}", 2)]))).AssertFieldRejectedAsync("snapshot");
        await (await client.PostGoalAsync(WithSnapshot([new(new string('a', GoalValidation.MaxEntityIdLength + 1), 1)]))).AssertFieldRejectedAsync("snapshot");
        await (await client.PostGoalAsync(WithSnapshot([new("material-1", 1)]))).AssertFieldRejectedAsync("goals");
    }

    // ----- Farming locations and type/field mismatches -----

    private static CreateCombinedGoalsRequest RankWithLocations(IEnumerable<string> ids) =>
        Request(Rank(15) with
        {
            Config = Rank(15).Config with { FarmingLocationIds = [.. ids.Select(CampaignBattleId.From)] },
        });

    [Fact]
    public async Task FarmingLocationsAreBoundedInCount()
    {
        var (client, _) = await ClientAsync();
        var battles = catalog.CampaignBattleViews.Select(battle => battle.Id).ToList();

        await (await client.PostGoalAsync(RankWithLocations(battles.Take(GoalValidation.MaxFarmingLocations + 1)))).AssertFieldRejectedAsync("config");
        (await client.PostGoalAsync(RankWithLocations(battles.Take(GoalValidation.MaxFarmingLocations)))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateRejectsAFarmingLocationOutsideTheCatalog()
    {
        var (client, _) = await ClientAsync();

        await AssertTargetRejectedAsync(await client.PostGoalAsync(RankWithLocations(["node-1"])), "campaign battle");
    }

    [Fact]
    public async Task CreateRejectsFieldsThatDoNotApplyToTheGoalType()
    {
        var (client, _) = await ClientAsync();
        var battle = catalog.CampaignBattleViews[0].Id;
        var ascension = new CombinedGoalSpec(
            GoalType.Ascension,
            new CreateGoalConfigRequest(
                Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.UncommonTwoStars),
                FarmingLocationIds: [CampaignBattleId.From(battle)]),
            []);
        var rankWithUpgrade = Rank(15) with
        {
            Config = Rank(15).Config with { Upgrade = new UpgradeTargetRequest([new("upgHpC001", 1)]) },
        };

        await AssertTargetRejectedAsync(await client.PostGoalAsync(Request(ascension)), "takes no farming locations");
        await AssertTargetRejectedAsync(await client.PostGoalAsync(Request(rankWithUpgrade)), "Only an Upgrade goal");
    }

    [Fact]
    public async Task UpgradeTargetsAreBoundedInCount()
    {
        var (client, _) = await ClientAsync();
        var targets = Enumerable.Range(0, GoalValidation.MaxResourcesPerGoal + 1)
            .Select(i => new UpgradeMaterialTargetRequest($"upgrade{i}", 1)).ToList();
        var upgrade = new CombinedGoalSpec(GoalType.Upgrade, new CreateGoalConfigRequest(Upgrade: new UpgradeTargetRequest(targets)), []);

        await (await client.PostGoalAsync(Request(upgrade))).AssertFieldRejectedAsync("config");
    }

    [Fact]
    public async Task EditBoundsAndCatalogChecksFarmingLocations()
    {
        var (client, _) = await ClientAsync();
        var goal = (await (await client.PostGoalAsync(Request(Rank(15)))).Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(Ct))!.Goals.Single();
        var battles = catalog.CampaignBattleViews.Select(battle => CampaignBattleId.From(battle.Id)).ToList();

        await (await client.PutDetailsAsync(goal.GoalId, new UpdateGoalRequest(null, [.. battles.Take(GoalValidation.MaxFarmingLocations + 1)])))
            .AssertFieldRejectedAsync("farmingLocationIds");
        await (await client.PutDetailsAsync(goal.GoalId, new UpdateGoalRequest(null, [CampaignBattleId.From("node-1")])))
            .AssertFieldRejectedAsync("farmingLocationIds");
        (await client.PutDetailsAsync(goal.GoalId, new UpdateGoalRequest(null, [.. battles.Take(GoalValidation.MaxFarmingLocations)])))
            .EnsureSuccessStatusCode();
    }

    // ----- DependsOnIndex -----

    [Fact]
    public async Task DependsOnIndexMustBeDistinct()
    {
        var (client, _) = await ClientAsync();
        var unlock = new CombinedGoalSpec(GoalType.Unlock, new CreateGoalConfigRequest(), []);

        await (await client.PostGoalAsync(Request(unlock, Rank(15) with { DependsOnIndex = [0, 0] }))).AssertFieldRejectedAsync("dependsOnIndex");
        await (await client.PostGoalAsync(Request(unlock, Rank(15) with { DependsOnIndex = [1] }))).AssertFieldRejectedAsync("dependsOnIndex");
        (await client.PostGoalAsync(Request(unlock, Rank(15) with { DependsOnIndex = [0] }))).EnsureSuccessStatusCode();
    }

    // ----- Project cap -----

    [Fact]
    public async Task ProjectCreationStopsAtTheCap()
    {
        var (client, subject) = await ClientAsync();
        await GoalsTestHelpers.SeedProjectsUpToAsync(factory, subject, ProjectValidation.MaxProjects - 1);

        (await client.PostAsJsonAsync("/api/v1/projects", new CreateProjectRequest("Last", null, null), Ct)).EnsureSuccessStatusCode();
        var rejected = await client.PostAsJsonAsync("/api/v1/projects", new CreateProjectRequest("One more", null, null), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(CreateProjectEndpoint.ProjectLimitReached, await rejected.ReadIssueCodeAsync());
    }
}
