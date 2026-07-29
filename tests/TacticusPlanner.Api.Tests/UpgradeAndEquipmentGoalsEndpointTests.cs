using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;

namespace TacticusPlanner.Api.Tests;

/// <summary>Coverage for the two goal types re-introduced/added alongside Rank/Ascension/Ability/Unlock:
/// <c>Upgrade</c> (farm one or more specific materials to a target quantity, scoped to the owning unit's
/// own requirements) and <c>UpgradeItem</c> (level up a specific piece of equipment, a new
/// "Item" entity type with no Character/Mow owner). Test ids (<c>blackTerminator</c>,
/// <c>astraOrdnanceBattery</c>, <c>upgHpC014</c>/<c>upgHpC015</c>, <c>I_Block_C002</c>) are real served
/// game-catalog entries — this project seeds its test catalog from the same data files production serves
/// (see <see cref="GoalsEndpointTests"/>'s own fixtures for the established precedent of using real ids
/// rather than a mocked catalog).</summary>
public sealed class UpgradeAndEquipmentGoalsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    // Present in blackTerminator's Stone1 AND Stone2 rank-up-upgrade lists (units-blacklegion.json).
    private const string CharacterRelevantUpgradeId = "upgHpC014";

    // Present in astraOrdnanceBattery's primaryAbility recipes[0] and recipes[2] (units-astramilitarum.json).
    private const string MowRelevantUpgradeId = "upgHpC015";

    // A 3-level equipment item (equipment-block.json) — valid target levels are 2 and 3.
    private const string EquipmentId = "I_Block_C002";

    private static readonly CreateGoalRequest RankGoal = new(
        "character",
        "blackTerminator",
        "rank",
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)),
        null
    );

    private static readonly CreateGoalRequest MowAbilityGoal = new(
        "mow",
        "astraOrdnanceBattery",
        "ability",
        new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3)),
        null
    );

    [Fact]
    public async Task CreateUpgradeGoalWithRelevantMaterialIsAccepted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with
            {
                GoalType = "upgrade",
                Config = new CreateGoalConfigRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeItemTargetRequest(CharacterRelevantUpgradeId, 3)])),
            },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal("Upgrade", created.GoalType);
        var target = Assert.Single(created.Config.Upgrade!.Targets);
        Assert.Equal(CharacterRelevantUpgradeId, target.UpgradeId);
        Assert.Equal(3, target.Quantity);
    }

    [Fact]
    public async Task CreateUpgradeGoalForMowWithRelevantMaterialIsAccepted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            MowAbilityGoal with
            {
                GoalType = "upgrade",
                Config = new CreateGoalConfigRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeItemTargetRequest(MowRelevantUpgradeId, 2)])),
            },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal("Mow", created.EntityType);
        Assert.Equal("Upgrade", created.GoalType);
    }

    [Fact]
    public async Task CreateUpgradeGoalWithIrrelevantMaterialIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with
            {
                GoalType = "upgrade",
                Config = new CreateGoalConfigRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeItemTargetRequest("not-a-real-upgrade-id", 1)])),
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUpgradeGoalWithDuplicateTargetsIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with
            {
                GoalType = "upgrade",
                Config = new CreateGoalConfigRequest(Upgrade: new UpgradeTargetRequest(
                [
                    new UpgradeItemTargetRequest(CharacterRelevantUpgradeId, 1),
                    new UpgradeItemTargetRequest(CharacterRelevantUpgradeId, 2),
                ])),
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUpgradeGoalWithNonPositiveQuantityIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with
            {
                GoalType = "upgrade",
                Config = new CreateGoalConfigRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeItemTargetRequest(CharacterRelevantUpgradeId, 0)])),
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUpgradeEquipmentGoalWithValidLevelIsAccepted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "item",
                EquipmentId,
                "upgradeitem",
                new CreateGoalConfigRequest(Item: new ItemTargetRequest(3)),
                null
            ),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal("Item", created.EntityType);
        Assert.Equal("UpgradeItem", created.GoalType);
        Assert.Equal(3, created.Config.Item!.TargetLevel);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task CreateUpgradeEquipmentGoalWithOutOfRangeLevelIsRejected(int targetLevel)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "item", EquipmentId, "upgradeitem",
                new CreateGoalConfigRequest(Item: new ItemTargetRequest(targetLevel)), null),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUpgradeEquipmentGoalWithUnknownEquipmentIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "item", "not-a-real-equipment-id", "upgradeitem",
                new CreateGoalConfigRequest(Item: new ItemTargetRequest(2)), null),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("item", "rank")]
    [InlineData("character", "upgradeitem")]
    [InlineData("mow", "upgradeitem")]
    public async Task MismatchedEntityAndGoalTypeIsRejected(string entityType, string goalType)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with { EntityType = entityType, GoalType = goalType, EntityId = EquipmentId },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCombinedUpgradeGoalIsAccepted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals/combined",
            new CreateCombinedGoalsRequest(
                "character",
                "blackTerminator",
                null,
                [
                    new CombinedGoalSpec(
                        "upgrade",
                        new CreateGoalConfigRequest(
                            Upgrade: new UpgradeTargetRequest([new UpgradeItemTargetRequest(CharacterRelevantUpgradeId, 5)])),
                        [])
                ]
            ),
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        var goal = Assert.Single(created.Goals);
        Assert.Equal("Upgrade", goal.GoalType);
        Assert.Equal(5, goal.Config.Upgrade!.Targets[0].Quantity);
    }
}
