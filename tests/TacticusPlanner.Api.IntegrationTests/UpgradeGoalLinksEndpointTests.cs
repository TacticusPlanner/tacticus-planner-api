using System.Net;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>An Upgrade goal's <c>linkedGoalIds</c>: the same unit's Active/Paused Rank (Character) or Ability
/// (Machine of War) goals it pre-farms for. Ids are real served catalog entries, as in
/// <see cref="UpgradeGoalsEndpointTests"/>.</summary>
public sealed class UpgradeGoalLinksEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string CharacterId = "blackTerminator";
    private const string OtherCharacterId = "ultraInceptorSgt";

    // A base material only reachable through a crafted upgrade on blackTerminator's ladder.
    private const string CharacterIngredientUpgradeId = "upgDmgC010";

    [Fact]
    public async Task AnUpgradeGoalLinksToTheUnitsRankGoalOnCreateAndEdit()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var rank = await client.CreateGoalAsync(RankGoal(CharacterId));

        var created = await PostUpgradeAsync(client, [rank.GoalId]);
        created.EnsureSuccessStatusCode();
        var upgrade = (await created.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(Ct))!.Goals.Single();
        Assert.Equal([rank.GoalId], upgrade.LinkedGoalIds);
        Assert.Empty(rank.LinkedGoalIds);

        var unlinked = await client.EditGoalAsync(upgrade.GoalId, new EditGoalRequest(LinkedGoalIds: []));
        Assert.Empty((await unlinked.ReadEditedGoalAsync()).LinkedGoalIds);

        var relinked = await client.EditGoalAsync(upgrade.GoalId, new EditGoalRequest(LinkedGoalIds: [rank.GoalId]));
        Assert.Equal([rank.GoalId], (await relinked.ReadEditedGoalAsync()).LinkedGoalIds);
    }

    [Fact]
    public async Task ALinkToAnotherUnitsGoalIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var otherRank = await client.CreateGoalAsync(RankGoal(OtherCharacterId));

        var response = await PostUpgradeAsync(client, [otherRank.GoalId]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain((await client.ListGoalsAsync()).Goals, goal => goal.GoalType == GoalType.Upgrade);
    }

    [Fact]
    public async Task OnlyAnUpgradeGoalCanLink()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var low = await client.CreateGoalAsync(RankGoal(CharacterId, end: 3));
        var high = await client.CreateGoalAsync(RankGoal(CharacterId, end: 5));

        var response = await client.EditGoalAsync(high.GoalId, new EditGoalRequest(LinkedGoalIds: [low.GoalId]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletingALinkedGoalRemovesTheLink()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var rank = await client.CreateGoalAsync(RankGoal(CharacterId));
        var upgrade = (await (await PostUpgradeAsync(client, [rank.GoalId])).Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(Ct))!
            .Goals.Single();

        (await client.DeleteAsync($"/api/v1/goals/{rank.GoalId}", Ct)).EnsureSuccessStatusCode();

        var remaining = (await client.ListGoalsAsync()).Goals.Single(goal => goal.GoalId == upgrade.GoalId);
        Assert.Empty(remaining.LinkedGoalIds);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateGoalRequest RankGoal(string characterId, int end = 5) => new(
        GoalEntityType.Character,
        characterId,
        GoalType.Rank,
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, end, false, 0)),
        null);

    private static Task<HttpResponseMessage> PostUpgradeAsync(HttpClient client, List<Guid> linkedGoalIds) =>
        client.PostGoalAsync(new CreateCombinedGoalsRequest(
            GoalEntityType.Character,
            CharacterId,
            null,
            [
                new CombinedGoalSpec(
                    GoalType.Upgrade,
                    new CreateGoalConfigRequest(Upgrade: new UpgradeTargetRequest(
                        [new UpgradeMaterialTargetRequest(CharacterIngredientUpgradeId, 4)],
                        new UpgradeRangeRequest(1, 5))),
                    [],
                    LinkedGoalIds: linkedGoalIds),
            ]));
}
