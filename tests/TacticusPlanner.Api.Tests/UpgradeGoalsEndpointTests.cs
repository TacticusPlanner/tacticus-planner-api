using System.Net;
using System.Net.Http.Json;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.Tests;

/// <summary>Coverage for Upgrade goals, which farm one or more specific materials scoped to the owning
/// unit's requirements. Test ids (<c>blackTerminator</c>, <c>astraOrdnanceBattery</c>, and
/// <c>upgHpC014</c>/<c>upgHpC015</c>) are real served
/// game-catalog entries — this project seeds its test catalog from the same data files production serves
/// (see <see cref="GoalsEndpointTests"/>'s own fixtures for the established precedent of using real ids
/// rather than a mocked catalog).</summary>
public sealed class UpgradeGoalsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    // Present in blackTerminator's Stone1 AND Stone2 rank-up-upgrade lists (units-blacklegion.json).
    private const string CharacterRelevantUpgradeId = "upgHpC014";

    // Present in astraOrdnanceBattery's primaryAbility recipes[0] and recipes[2] (units-astramilitarum.json).
    private const string MowRelevantUpgradeId = "upgHpC015";

    // A base material blackTerminator's rank-up lists never name: it is only an ingredient of a crafted
    // upgrade on that ladder. A player farms this, not the crafted upgrade, so it must be targetable.
    private const string CharacterIngredientUpgradeId = "upgDmgC010";

    // Same, one level deeper — reachable only through a crafted upgrade nested inside another recipe.
    private const string CharacterNestedIngredientUpgradeId = "upgDmgU011";

    // The MoW equivalent: a base material only reachable by expanding astraOrdnanceBattery's recipes.
    private const string MowIngredientUpgradeId = "upgHpC006";

    // A real base material that is neither on blackTerminator's ladder nor in any of its recipes —
    // guards against relevance widening into "any upgrade in the game".
    private const string UnrelatedUpgradeId = "upgArmC001";

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
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(CharacterRelevantUpgradeId, 3)])),
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
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(MowRelevantUpgradeId, 2)])),
            },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal("Mow", created.EntityType);
        Assert.Equal("Upgrade", created.GoalType);
    }

    [Theory]
    [InlineData(CharacterIngredientUpgradeId)]
    [InlineData(CharacterNestedIngredientUpgradeId)]
    public async Task CreateUpgradeGoalWithDecomposedIngredientIsAccepted(string upgradeId)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with
            {
                GoalType = "upgrade",
                Config = new CreateGoalConfigRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(upgradeId, 4)])),
            },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        var target = Assert.Single(created.Config.Upgrade!.Targets);
        Assert.Equal(upgradeId, target.UpgradeId);
    }

    [Fact]
    public async Task CreateUpgradeGoalForMowWithDecomposedIngredientIsAccepted()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            MowAbilityGoal with
            {
                GoalType = "upgrade",
                Config = new CreateGoalConfigRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(MowIngredientUpgradeId, 2)])),
            },
            TestContext.Current.CancellationToken
        );
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal("Mow", created.EntityType);
        Assert.Equal(MowIngredientUpgradeId, created.Config.Upgrade!.Targets[0].UpgradeId);
    }

    [Theory]
    [InlineData("not-a-real-upgrade-id")]
    [InlineData(UnrelatedUpgradeId)]
    public async Task CreateUpgradeGoalWithIrrelevantMaterialIsRejected(string upgradeId)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            RankGoal with
            {
                GoalType = "upgrade",
                Config = new CreateGoalConfigRequest(
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(upgradeId, 1)])),
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
                    new UpgradeMaterialTargetRequest(CharacterRelevantUpgradeId, 1),
                    new UpgradeMaterialTargetRequest(CharacterRelevantUpgradeId, 2),
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
                    Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(CharacterRelevantUpgradeId, 0)])),
            },
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
                            Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(CharacterRelevantUpgradeId, 5)])),
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

    private static CreateGoalRequest UpgradeGoal(string entityType, string entityId, UpgradeTargetRequest upgrade) =>
        new(entityType, entityId, "upgrade", new CreateGoalConfigRequest(Upgrade: upgrade), null);

    private static UpgradeRangeRequest Range(int start, int end) => new(start, end);

    private static readonly List<UpgradeMaterialTargetRequest> MowTargets = [new(MowRelevantUpgradeId, 2)];

    [Fact]
    public async Task CharacterUpgradeGoalWithRankRangeRoundTrips()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            UpgradeGoal("character", "ultraApothecary",
                new UpgradeTargetRequest([new("upgArmC002", 5)], RankRange: Range((int)UnitRank.Stone2, (int)UnitRank.Iron1 + 1))),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken);
        var fetched = await client.GetFromJsonAsync<GoalDetailResponse>(
            $"/api/v1/me/goals/{created!.GoalId}", TestContext.Current.CancellationToken);

        var upgrade = fetched!.Config.Upgrade!;
        Assert.Equal(new UpgradeRangeResponse(1, 4), upgrade.RankRange);
        Assert.Null(upgrade.ActiveRange);
        Assert.Null(upgrade.PassiveRange);
    }

    [Fact]
    public async Task MowUpgradeGoalAcceptsOneOrBothAbilityRanges()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var activeOnly = await PostUpgradeAsync(client, new UpgradeTargetRequest(MowTargets, ActiveRange: Range(1, 5)));
        var both = await PostUpgradeAsync(await GoalsTestHelpers.CreateProvisionedClientAsync(factory), new UpgradeTargetRequest(MowTargets, ActiveRange: Range(1, 5), PassiveRange: Range(2, 4)));

        activeOnly.EnsureSuccessStatusCode();
        both.EnsureSuccessStatusCode();
        var a = (await activeOnly.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken))!.Config.Upgrade!;
        Assert.Equal(new UpgradeRangeResponse(1, 5), a.ActiveRange);
        Assert.Null(a.PassiveRange);
        var b = (await both.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken))!.Config.Upgrade!;
        Assert.Equal(new UpgradeRangeResponse(2, 4), b.PassiveRange);
    }

    [Fact]
    public async Task UpgradeGoalWithoutRangesReadsBackNullGroups()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await PostUpgradeAsync(client, new UpgradeTargetRequest(MowTargets));

        var upgrade = (await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken))!.Config.Upgrade!;
        Assert.Null(upgrade.RankRange);
        Assert.Null(upgrade.ActiveRange);
        Assert.Null(upgrade.PassiveRange);
    }

    public static TheoryData<string, UpgradeTargetRequest> InvalidRangeRequests => new()
    {
        { "mow", new UpgradeTargetRequest(MowTargets, RankRange: Range(0, 2)) },
        { "mow", new UpgradeTargetRequest(MowTargets, ActiveRange: Range(3, 3)) },
        { "mow", new UpgradeTargetRequest(MowTargets, ActiveRange: Range(0, 2)) },
        { "mow", new UpgradeTargetRequest(MowTargets, PassiveRange: Range(1, 99)) },
        { "mow", new UpgradeTargetRequest(MowTargets, ActiveRange: new UpgradeRangeRequest(1, null)) },
        { "character", new UpgradeTargetRequest([new("upgArmC002", 1)], ActiveRange: Range(1, 2)) },
        { "character", new UpgradeTargetRequest([new("upgArmC002", 1)], RankRange: Range(4, 2)) },
        { "character", new UpgradeTargetRequest([new("upgArmC002", 1)], RankRange: Range(-1, 2)) },
        { "character", new UpgradeTargetRequest([new("upgArmC002", 1)], RankRange: Range(0, 999)) },
    };

    [Theory]
    [MemberData(nameof(InvalidRangeRequests))]
    public async Task InvalidUpgradeRangesAreRejectedOnCreate(string entityType, UpgradeTargetRequest upgrade)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var entityId = entityType == "mow" ? "astraOrdnanceBattery" : "ultraApothecary";

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals", UpgradeGoal(entityType, entityId, upgrade), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<HttpResponseMessage> PostUpgradeAsync(HttpClient client, UpgradeTargetRequest upgrade) =>
        client.PostAsJsonAsync(
            "/api/v1/me/goals", UpgradeGoal("mow", "astraOrdnanceBattery", upgrade), TestContext.Current.CancellationToken);
}
