using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TacticusPlanner.Api.Features.LegendaryEventPlans;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Covers <c>add-legendary-event-run-inputs</c>'s <c>legendary-event-plans</c> delta: run inputs per run
/// (<c>PUT …/runs/{run}</c>) and Maybe clear / Stop here annotations per objective cell (<c>PUT …/annotations</c>),
/// both under the plan's revision contract, and their place in the served plan. Also covers
/// <c>add-legendary-event-mission-tracking</c>'s delta: the completed mission arrays on run inputs.
/// </summary>
public sealed class LegendaryEventRunInputsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string EventId = "votanUthar";
    private const string PlanUrl = $"/api/v1/me/legendary-event-plans/{EventId}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunWriteCreatesThePlan()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await WriteRunAsync(client, 2, new(0, 6, 0, false, 0)));

        Assert.Equal(1, plan.Revision);
        var run = Assert.Single(plan.Runs);
        Assert.Equal((2, 6, 0, false, 0), (run.Run, run.RegularMissions, run.PremiumMissions, run.BundlePurchased, run.CloseShards));
        Assert.NotEqual(default, run.UpdatedAt);
        Assert.Equivalent(plan, await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct)));
    }

    [Fact]
    public async Task ARunWriteTouchesOnlyItsRun()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(0, 10, 0, false, 0)));

        plan = await ReadPlanAsync(await WriteRunAsync(client, 3, new(plan.Revision, 10, 2, true, 25)));

        Assert.Equal(2, plan.Revision);
        Assert.Equal(
            [(1, 10, 0, false, 0), (3, 10, 2, true, 25)],
            plan.Runs.Select(run => (run.Run, run.RegularMissions, run.PremiumMissions, run.BundlePurchased, run.CloseShards)));
    }

    [Fact]
    public async Task RunsAreServedAscendingWithAbsentRunsOmitted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await WriteRunAsync(client, 3, new(0, 1, 0, false, 0)));
        await WriteRunAsync(client, 1, new(plan.Revision, 2, 0, false, 0));

        plan = await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct));

        Assert.Equal([1, 3], plan.Runs.Select(run => run.Run));
    }

    [Theory]
    [InlineData("1", 11, 0, 0, "regularMissions")]
    [InlineData("1", -1, 0, 0, "regularMissions")]
    [InlineData("1", 0, 11, 0, "premiumMissions")]
    [InlineData("1", 0, 0, 76, "closeShards")]
    [InlineData("1", 0, 0, -25, "closeShards")]
    [InlineData("4", 0, 0, 0, "run")]
    [InlineData("0", 0, 0, 0, "run")]
    [InlineData("two", 0, 0, 0, "run")]
    public async Task OutOfRangeRunInputsAreRejected(string run, int regular, int premium, int closeShards, string field)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            $"{PlanUrl}/runs/{run}", new UpdateLegendaryEventRunInputsRequest(0, regular, premium, false, closeShards), Ct);

        await AssertBadRequestAsync(response, field);
        Assert.Equal(0, (await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct))).Revision);
    }

    [Fact]
    public async Task PaidInputsAreStoredWhenPaidOptionsAreHidden()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(0, 0, 10, true, 75)));

        Assert.False(plan.ShowPaidOptions);
        Assert.Equal((10, true, 75), (plan.Runs[0].PremiumMissions, plan.Runs[0].BundlePurchased, plan.Runs[0].CloseShards));
    }

    [Fact]
    public async Task UnchangedRunInputsKeepTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(0, 10, 0, false, 0)));
        var stored = plan.Runs[0].UpdatedAt;

        plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(plan.Revision, 10, 0, false, 0)));

        Assert.Equal((1L, stored), (plan.Revision, plan.Runs[0].UpdatedAt));
    }

    [Fact]
    public async Task AllZeroInputsForAnAbsentRunCreateNothing()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await WriteRunAsync(client, 2, new(0, 0, 0, false, 0)));

        Assert.Equal(0, plan.Revision);
        Assert.Empty(plan.Runs);
        Assert.Equal(0, (await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct))).Revision);
    }

    [Fact]
    public async Task ZerosResetAStoredRun()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(0, 4, 0, false, 0)));

        plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(plan.Revision, 0, 0, false, 0)));

        Assert.Equal((2L, 0), (plan.Revision, plan.Runs[0].RegularMissions));
    }

    [Fact]
    public async Task AStaleRunWriteIsAConflict()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, new UpdateLegendaryEventPlanRequest(0, null, false), Ct));
        plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(plan.Revision, 3, 0, false, 0)));
        plan = await ReadPlanAsync(await WriteRunAsync(client, 2, new(plan.Revision, 3, 0, false, 0)));

        var conflict = await ReadConflictAsync(await WriteRunAsync(client, 1, new(plan.Revision - 1, 9, 0, false, 0)));

        Assert.Equal(LegendaryEventPlanIssueCodes.Stale, conflict.IssueCode);
        Assert.Equal(3, conflict.Plan.Revision);
        Assert.Equal(3, conflict.Plan.Runs[0].RegularMissions);
    }

    [Fact]
    public async Task CompletedMissionsAreStoredSortedOnANewPlan()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await WriteRunAsync(client, 2, new(0, 6, 0, false, 0, [3, 1])));

        Assert.Equal(1, plan.Revision);
        var run = Assert.Single(plan.Runs);
        Assert.Equal((2, 6, 0, false, 0), (run.Run, run.RegularMissions, run.PremiumMissions, run.BundlePurchased, run.CloseShards));
        Assert.Equal([1, 3], run.CompletedRegularMissions);
        Assert.Empty(run.CompletedPremiumMissions);
        var read = Assert.Single((await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct))).Runs);
        Assert.Equal([1, 3], read.CompletedRegularMissions);
        Assert.Empty(read.CompletedPremiumMissions);
    }

    [Fact]
    public async Task ARunWriteWithCompletedMissionsTouchesOnlyItsRun()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(0, 10, 0, false, 0, [0, 9])));
        plan = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, new UpdateLegendaryEventPlanRequest(plan.Revision, null, false), Ct));
        plan = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, new UpdateLegendaryEventPlanRequest(plan.Revision, "n", false), Ct));
        plan = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, new UpdateLegendaryEventPlanRequest(plan.Revision, "m", false), Ct));
        Assert.Equal(4, plan.Revision);

        plan = await ReadPlanAsync(await WriteRunAsync(client, 3, new(plan.Revision, 10, 2, true, 0, [], [7])));

        Assert.Equal(5, plan.Revision);
        Assert.Equal(
            [(1, 10, 0, "0,9", "", false), (3, 10, 2, "", "7", true)],
            plan.Runs.Select(run => (run.Run, run.RegularMissions, run.PremiumMissions,
                string.Join(',', run.CompletedRegularMissions), string.Join(',', run.CompletedPremiumMissions), run.BundlePurchased)));
    }

    [Theory]
    [InlineData(1, 0, new[] { 0, 2 }, new int[0], "regularMissions")]
    [InlineData(10, 0, new int[0], new[] { 0 }, "premiumMissions")]
    [InlineData(10, 10, new int[0], new[] { 10 }, "completedPremiumMissions")]
    [InlineData(10, 10, new int[0], new[] { -1 }, "completedPremiumMissions")]
    [InlineData(10, 10, new[] { 10 }, new int[0], "completedRegularMissions")]
    [InlineData(10, 10, new[] { 2, 2 }, new int[0], "completedRegularMissions")]
    [InlineData(10, 10, new int[0], new[] { 4, 1, 4 }, "completedPremiumMissions")]
    public async Task InvalidCompletedMissionsAreRejected(int regular, int premium, int[] completedRegular, int[] completedPremium, string field)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await WriteRunAsync(client, 1, new(0, regular, premium, false, 0, completedRegular, completedPremium));

        await AssertBadRequestAsync(response, field);
        Assert.Equal(0, (await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct))).Revision);
    }

    [Fact]
    public async Task CountBelowTicksLeavesAStoredRunUnchanged()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(0, 3, 0, false, 0, [0, 1])));

        await AssertBadRequestAsync(await WriteRunAsync(client, 1, new(plan.Revision, 1, 0, false, 0, [0, 2])), "regularMissions");

        plan = await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct));
        Assert.Equal((1L, 3), (plan.Revision, plan.Runs[0].RegularMissions));
        Assert.Equal([0, 1], plan.Runs[0].CompletedRegularMissions);
    }

    [Fact]
    public async Task AbsentCompletedArraysMeanNone()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(0, 10, 2, false, 0, [4], [1])));

        var response = await client.PutAsJsonAsync($"{PlanUrl}/runs/1", new
        {
            expectedRevision = plan.Revision,
            regularMissions = 10,
            premiumMissions = 2,
            bundlePurchased = false,
            closeShards = 0,
        }, Ct);
        plan = await ReadPlanAsync(response);

        Assert.Equal(2, plan.Revision);
        Assert.Empty(plan.Runs[0].CompletedRegularMissions);
        Assert.Empty(plan.Runs[0].CompletedPremiumMissions);
    }

    [Fact]
    public async Task UnchangedCompletedMissionsKeepTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(0, 10, 3, false, 0, [2, 5], [0])));
        var stored = plan.Runs[0].UpdatedAt;

        var same = await ReadPlanAsync(await WriteRunAsync(client, 1, new(plan.Revision, 10, 3, false, 0, [5, 2], [0])));
        Assert.Equal((1L, stored), (same.Revision, same.Runs[0].UpdatedAt));

        var ticked = await ReadPlanAsync(await WriteRunAsync(client, 1, new(plan.Revision, 10, 3, false, 0, [2, 5, 6], [0])));
        Assert.Equal(2, ticked.Revision);
        Assert.Equal([2, 5, 6], ticked.Runs[0].CompletedRegularMissions);

        var unticked = await ReadPlanAsync(await WriteRunAsync(client, 1, new(ticked.Revision, 10, 3, false, 0, [2, 5, 6])));
        Assert.Equal(3, unticked.Revision);
        Assert.Empty(unticked.Runs[0].CompletedPremiumMissions);
    }

    [Fact]
    public async Task UnchangedEmptyCompletedMissionsKeepTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(0, 10, 0, false, 0)));
        for (var revision = plan.Revision; revision < 6;)
        {
            plan = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, new UpdateLegendaryEventPlanRequest(revision, $"n{revision}", false), Ct));
            revision = plan.Revision;
        }

        plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(6, 10, 0, false, 0, [], [])));

        Assert.Equal(6, plan.Revision);
    }

    [Fact]
    public async Task MarkOneCellOnANewPlan()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await MarkAsync(client, new(0, "alpha", 6, [3], "stopHere")));

        Assert.Equal(1, plan.Revision);
        var annotation = Assert.Single(plan.Annotations);
        Assert.Equal(("alpha", 6, 3, "stopHere"), (annotation.LaneId, annotation.BattleIndex, annotation.ObjectiveId, annotation.Status));
        Assert.NotEqual(default, annotation.UpdatedAt);
    }

    [Fact]
    public async Task MarkAWholeBattleThenClearOneCell()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, new UpdateLegendaryEventPlanRequest(0, null, false), Ct));
        plan = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, new UpdateLegendaryEventPlanRequest(1, "n", false), Ct));

        plan = await ReadPlanAsync(await MarkAsync(client, new(plan.Revision, "alpha", 7, [0, 1, 2, 4], "maybeClear")));
        plan = await ReadPlanAsync(await MarkAsync(client, new(plan.Revision, "alpha", 7, [2], null)));

        Assert.Equal(4, plan.Revision);
        Assert.Equal([0, 1, 4], plan.Annotations.Select(annotation => annotation.ObjectiveId));
        Assert.All(plan.Annotations, annotation => Assert.Equal(("alpha", 7, "maybeClear"),
            (annotation.LaneId, annotation.BattleIndex, annotation.Status)));
    }

    [Fact]
    public async Task ChangingAMarkReplacesItsStatus()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await MarkAsync(client, new(0, "beta", 2, [1], "maybeClear")));

        plan = await ReadPlanAsync(await MarkAsync(client, new(plan.Revision, "beta", 2, [1], "stopHere")));

        Assert.Equal((2L, "stopHere"), (plan.Revision, Assert.Single(plan.Annotations).Status));
    }

    [Fact]
    public async Task AnnotationsAreOrderedByLaneBattleAndObjective()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await MarkAsync(client, new(0, "beta", 2, [1], "stopHere")));
        plan = await ReadPlanAsync(await MarkAsync(client, new(plan.Revision, "alpha", 9, [0], "maybeClear")));
        await MarkAsync(client, new(plan.Revision, "alpha", 3, [5], "maybeClear"));

        plan = await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct));

        Assert.Equal(
            [("alpha", 3, 5), ("alpha", 9, 0), ("beta", 2, 1)],
            plan.Annotations.Select(annotation => (annotation.LaneId, annotation.BattleIndex, annotation.ObjectiveId)));
    }

    [Theory]
    [InlineData("delta", 0, new[] { 0 }, "stopHere", "laneId")]
    [InlineData("alpha", 18, new[] { 0 }, "stopHere", "battleIndex")]
    [InlineData("alpha", -1, new[] { 0 }, "stopHere", "battleIndex")]
    [InlineData("alpha", 0, new[] { 6 }, "stopHere", "objectiveIds")]
    [InlineData("alpha", 0, new[] { -1 }, "stopHere", "objectiveIds")]
    [InlineData("alpha", 0, new int[0], "stopHere", "objectiveIds")]
    [InlineData("alpha", 0, new[] { 1, 1 }, "stopHere", "objectiveIds")]
    [InlineData("alpha", 0, new[] { 0, 1, 2, 3, 4, 5, 0 }, "stopHere", "objectiveIds")]
    [InlineData("alpha", 0, new[] { 0 }, "cleared", "status")]
    public async Task InvalidAnnotationWritesAreRejected(
        string laneId, int battleIndex, int[] objectiveIds, string status, string field)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await MarkAsync(client, new(0, laneId, battleIndex, objectiveIds, status));

        await AssertBadRequestAsync(response, field);
        Assert.Equal(0, (await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct))).Revision);
    }

    [Fact]
    public async Task UnchangedAnnotationsKeepTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await MarkAsync(client, new(0, "alpha", 6, [3], "stopHere")));

        var same = await ReadPlanAsync(await MarkAsync(client, new(plan.Revision, "alpha", 6, [3], "stopHere")));
        var clearUnmarked = await ReadPlanAsync(await MarkAsync(client, new(plan.Revision, "alpha", 6, [4], null)));

        Assert.Equal((1L, 1L), (same.Revision, clearUnmarked.Revision));
        Assert.Single(clearUnmarked.Annotations);
    }

    [Fact]
    public async Task ClearingOnAMissingPlanCreatesNothing()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await MarkAsync(client, new(0, "alpha", 6, [3], null)));

        Assert.Equal(0, plan.Revision);
        Assert.Empty(plan.Annotations);
    }

    [Fact]
    public async Task AStaleAnnotationWriteIsAConflict()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await MarkAsync(client, new(0, "alpha", 6, [3], "stopHere")));

        var conflict = await ReadConflictAsync(await MarkAsync(client, new(plan.Revision + 1, "alpha", 6, [3], null)));

        Assert.Equal(LegendaryEventPlanIssueCodes.Stale, conflict.IssueCode);
        Assert.Single(conflict.Plan.Annotations);
    }

    [Fact]
    public async Task EmptyPlanServesEmptyRunsAndAnnotations()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct));

        Assert.Empty(plan.Runs);
        Assert.Empty(plan.Annotations);
    }

    [Fact]
    public async Task ServedJsonUsesTheContractFieldNames()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var plan = await ReadPlanAsync(await WriteRunAsync(client, 1, new(0, 1, 0, false, 0)));
        await MarkAsync(client, new(plan.Revision, "alpha", 0, [0], "maybeClear"));

        using var document = JsonDocument.Parse(await client.GetStringAsync(PlanUrl, Ct));
        var root = document.RootElement;
        Assert.Equal(
            ["run", "regularMissions", "premiumMissions", "completedRegularMissions", "completedPremiumMissions",
                "bundlePurchased", "closeShards", "updatedAt"],
            root.GetProperty("runs")[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(["laneId", "battleIndex", "objectiveId", "status", "updatedAt"],
            root.GetProperty("annotations")[0].EnumerateObject().Select(property => property.Name));
    }

    private static Task<HttpResponseMessage> WriteRunAsync(
        HttpClient client, int run, UpdateLegendaryEventRunInputsRequest request) =>
        client.PutAsJsonAsync($"{PlanUrl}/runs/{run}", request, Ct);

    private static Task<HttpResponseMessage> MarkAsync(HttpClient client, UpdateLegendaryEventAnnotationsRequest request) =>
        client.PutAsJsonAsync($"{PlanUrl}/annotations", request, Ct);

    private static async Task<LegendaryEventPlanResponse> ReadPlanAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        var plan = await response.Content.ReadFromJsonAsync<LegendaryEventPlanResponse>(Ct);
        Assert.NotNull(plan);
        return plan;
    }

    private static async Task<LegendaryEventPlanConflictResponse> ReadConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.Content.ReadFromJsonAsync<LegendaryEventPlanConflictResponse>(Ct);
        Assert.NotNull(conflict);
        return conflict;
    }

    private static async Task AssertBadRequestAsync(HttpResponseMessage response, string field)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var document = JsonDocument.Parse(body);
        Assert.Contains(field,
            document.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name),
            StringComparer.OrdinalIgnoreCase);
    }
}
