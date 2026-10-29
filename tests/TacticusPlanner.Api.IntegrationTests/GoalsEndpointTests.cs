using System.Net;
using System.Text;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class GoalsEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private static readonly CreateGoalRequest RankGoal = new(
        GoalEntityType.Character,
        "blackTerminator",
        GoalType.Rank,
        new CreateGoalConfigRequest(Rank: new RankTargetRequest(1, false, 0, 5, false, 0)),
        null
    );

    [Fact]
    public async Task OpenApiDocumentReplacesAscensionFarmingWithAcquisitionSources()
    {
        var client = factory.CreateClient();

        var document = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("AscensionFarming", document, StringComparison.Ordinal);
        Assert.Contains("acquisitionSources", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnauthenticatedListIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.GetAsync("/api/v1/goals", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnprovisionedAccountListIsNotFound()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, $"goals-{Guid.NewGuid()}");

        var response = await client.GetAsync("/api/v1/goals", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalThenAppearsInList()
    {
        // GoalsTestHelpers only hits GET /api/v1/account — no ListProjects call — so a goal landing Active in
        // its default project here also proves the default project is seeded during account provisioning,
        // not just lazily by the Projects list endpoint.
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var createResponse = await client.PostGoalAsync(RankGoal);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.ReadCreatedGoalAsync();
        Assert.NotNull(created);
        Assert.Equal(GoalEntityType.Character, created.EntityType);
        Assert.Equal(GoalType.Rank, created.GoalType);
        Assert.Equal(GoalStatus.Active, created.Status);
        Assert.True(created.Revision > 0);

        var listResponse = await client.GetAsync("/api/v1/goals", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<ListGoalsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(list);
        Assert.Contains(list.Goals, goal => goal.GoalId == created.GoalId);

        var defaultProject = await GetDefaultProjectAsync(client);
        Assert.Equal([defaultProject.ProjectId], created.ProjectIds);
    }

    [Fact]
    public async Task CreateGoalWithMultipleProjectIdsAddsToEachProject()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var otherProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(otherProject);

        var response = await client.PostGoalAsync(RankGoal with
        {
            Projects =
                [
                    new ProjectPriorityRequest(defaultProject.ProjectId),
                    new ProjectPriorityRequest(otherProject.ProjectId),
                ],
        });
        response.EnsureSuccessStatusCode();
        var created = await response.ReadCreatedGoalAsync();

        Assert.NotNull(created);
        Assert.Equal(
            new HashSet<Guid> { defaultProject.ProjectId, otherProject.ProjectId },
            created.ProjectIds.ToHashSet()
        );
        // Status is independent of membership: the goal starts Active whichever projects it joins.
        Assert.Equal(GoalStatus.Active, created.Status);

        var otherMembers = await client.GetProjectGoalsAsync(otherProject.ProjectId);
        Assert.NotNull(otherMembers);
        Assert.Contains(otherMembers.Goals, entry => entry.Goal.GoalId == created.GoalId);
    }

    [Fact]
    public async Task CreateGoalWithExplicitPriorityIsHonoredOverAutoAppend()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        // A different goal type for the same character (Ascension, not Rank) so it doesn't trip the
        // one-active-or-paused-per-(entity,type) constraint — occupies priority 1 in the default
        // project via the normal auto-append path, so the Rank goal's explicit priority below is a
        // deliberate insert-at-the-top, not a coincidence.
        var ascensionGoalResponse = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "blackTerminator",
                GoalType.Ascension,
                new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar)),
                null
            ));
        ascensionGoalResponse.EnsureSuccessStatusCode();

        var response = await client.PostGoalAsync(RankGoal with { Projects = [new ProjectPriorityRequest(defaultProject.ProjectId)] });
        response.EnsureSuccessStatusCode();
        var created = await response.ReadCreatedGoalAsync();
        Assert.NotNull(created);

        var members = await client.GetProjectGoalsAsync(defaultProject.ProjectId);
        Assert.NotNull(members);
        var priority = Assert.Single(members.Goals, entry => entry.Goal.GoalId == created.GoalId).Goal.GlobalPriority;
        Assert.Equal(2, priority);
    }

    [Fact]
    public async Task CreateGoalProjectMembershipUsesAutomaticPriority()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);

        var response = await client.PostGoalAsync(RankGoal with { Projects = [new ProjectPriorityRequest(defaultProject.ProjectId)] });

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateGoalInCustomProjectStartsActive()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var otherProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(otherProject);

        var response = await client.PostGoalAsync(RankGoal with { Projects = [new ProjectPriorityRequest(otherProject.ProjectId)] });
        response.EnsureSuccessStatusCode();
        var created = await response.ReadCreatedGoalAsync();

        Assert.NotNull(created);
        Assert.Equal(GoalStatus.Active, created.Status);
    }

    [Fact]
    public async Task CreateGoalWithStartPausedStartsPaused()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(RankGoal with { StartPaused = true });
        response.EnsureSuccessStatusCode();
        var created = await response.ReadCreatedGoalAsync();

        Assert.NotNull(created);
        Assert.Equal(GoalStatus.Paused, created.Status);
    }

    [Fact]
    public async Task CreateGoalWithoutStartPausedStartsActive()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(RankGoal);
        response.EnsureSuccessStatusCode();
        var created = await response.ReadCreatedGoalAsync();

        Assert.NotNull(created);
        Assert.Equal(GoalStatus.Active, created.Status);
    }

    [Fact]
    public async Task CreateStartPausedGoalInOccupiedSlotIsRejected()
    {
        // Paused occupies a project goal-type slot exactly as Active does, so asking for a paused goal is
        // not a way around the "at most one in flight" invariant (project-goal-slots).
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);

        var first = await client.PostGoalAsync(RankGoal with { Projects = [new ProjectPriorityRequest(defaultProject.ProjectId)] });
        first.EnsureSuccessStatusCode();
        var active = await first.ReadCreatedGoalAsync();
        Assert.NotNull(active);
        Assert.Equal(GoalStatus.Active, active.Status);

        var response = await client.PostGoalAsync(RankGoal with
        {
            Projects = [new ProjectPriorityRequest(defaultProject.ProjectId)],
            StartPaused = true,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var conflict = await response.ReadProblemAsync();
        Assert.Equal("slotOccupied", conflict["issueCode"]!.GetValue<string>());
        Assert.Equal(active.GoalId, conflict["existingGoalId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task CreateUnlockGoalWithUnavailableCampaignAcquisitionSourceIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "blackTerminator",
                GoalType.Unlock,
                new CreateGoalConfigRequest(
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Campaign, ["not-a-real-battle"])]
                ),
                null
            ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAscensionGoalWithUnavailableCampaignAcquisitionSourceIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "blackTerminator",
                GoalType.Ascension,
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar),
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Campaign, ["not-a-real-battle"])]
                ),
                null
            ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // "deathBlightlord" has a regular-shard campaign node at DGS06 (shards_deathBlightlord) and a
    // mythic-shard node at DGE25 (mythicShards_deathBlightlord). A Campaign acquisition source's ids are
    // validated against the union of the character's regular and mythic shard nodes — the server no
    // longer distinguishes which slot a goal type needs (the client gates that), so either node is a
    // valid Campaign id for either goal type.
    [Fact]
    public async Task CreateUnlockGoalAcceptsAMythicShardNodeAsACampaignSource()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "deathBlightlord",
                GoalType.Unlock,
                new CreateGoalConfigRequest(
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Campaign, ["DGE25"])]
                ),
                null
            ));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateUnlockGoalAcceptsAKnownShardNodeAsACampaignSource()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "deathBlightlord",
                GoalType.Unlock,
                new CreateGoalConfigRequest(
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Campaign, ["DGS06"])]
                ),
                null
            ));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateAscensionGoalAcceptsRegularAndMythicShardNodesInOneCampaignSource()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "deathBlightlord",
                GoalType.Ascension,
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar),
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Campaign, ["DGS06", "DGE25"])]
                ),
                null
            ));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateAscensionGoalAcceptsAnOnslaughtSource()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "deathBlightlord",
                GoalType.Ascension,
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar),
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Onslaught, [])]
                ),
                null
            ));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateUnlockGoalRejectsAnOnslaughtSource()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "deathBlightlord",
                GoalType.Unlock,
                new CreateGoalConfigRequest(
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Onslaught, [])]
                ),
                null
            ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMowAscensionGoalRejectsAnOnslaughtSource()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Mow,
                "astraOrdnanceBattery",
                GoalType.Ascension,
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar),
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Onslaught, [])]
                ),
                null
            ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAscensionGoalRejectsAnOnslaughtSourceWithIds()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "deathBlightlord",
                GoalType.Ascension,
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar),
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Onslaught, ["unexpected"])]
                ),
                null
            ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalRejectsAnUnknownAcquisitionSourceKind()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostMutatedGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "deathBlightlord",
                GoalType.Ascension,
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar),
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Campaign, [])]
                ),
                null
            ),
            request => request["goals"]![0]!["config"]!["acquisitionSources"]![0]!["kind"] = "Auction");

        await response.AssertFieldRejectedAsync("kind");
        Assert.Empty((await client.ListGoalsAsync()).Goals);
    }

    [Fact]
    public async Task CreateGoalRejectsANullAcquisitionSourceElement()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var json = """
            {
              "entityType": "character",
              "entityId": "deathBlightlord",
              "goalType": "ascension",
              "config": {
                "progression": { "start": "Common:None", "end": "Common:OneStar" },
                "acquisitionSources": [null]
              }
            }
            """;

        var response = await client.PostAsync(
            "/api/v1/goals",
            new StringContent(json, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalRejectsANullAcquisitionSourceIdsList()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var json = """
            {
              "entityType": "character",
              "entityId": "deathBlightlord",
              "goalType": "ascension",
              "config": {
                "progression": { "start": "Common:None", "end": "Common:OneStar" },
                "acquisitionSources": [{ "kind": "Campaign", "ids": null }]
              }
            }
            """;

        var response = await client.PostAsync(
            "/api/v1/goals",
            new StringContent(json, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAscensionGoalRejectsAMalformedShopOfferId()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "deathBlightlord",
                GoalType.Ascension,
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar),
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, ["not-a-shop-offer"])]
                ),
                null
            ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAscensionGoalRejectsAShopOfferNamingAnUnknownShop()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "deathBlightlord",
                GoalType.Ascension,
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar),
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, ["not-a-real-shop:shards_deathBlightlord"])]
                ),
                null
            ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCharacterUnlockGoalAcceptsAShopSource()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "eldarFarseer",
                GoalType.Unlock,
                new CreateGoalConfigRequest(
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, ["guild:shards_eldarFarseer"])]
                ),
                null
            ));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateUnlockGoalForAShopOnlyCharacterIsAccepted()
    {
        // Kharn has no campaign shard nodes; the Guild War shop is his only shard source.
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "worldKharn",
                GoalType.Unlock,
                new CreateGoalConfigRequest(
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, ["war:shards_worldKharn"])]
                ),
                null
            ));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateMowUnlockGoalRejectsAShopSource()
    {
        // Unlock is Character-only (see CreateGoalDeferredGoalTypeOrEntityTypeIsRejected), so this proves
        // Shop-source gating for MoW goals via Ascension instead.
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Mow,
                "astraOrdnanceBattery",
                GoalType.Ascension,
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar),
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, ["guild:shards_eldarFarseer"])]
                ),
                null
            ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalUnknownEntityTypeIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostMutatedGoalAsync(RankGoal, request => request["entityType"] = "spaceship");

        await response.AssertFieldRejectedAsync("entityType");
        Assert.Empty((await client.ListGoalsAsync()).Goals);
    }

    [Theory]
    [InlineData("0", "rank")]
    [InlineData("999", "rank")]
    [InlineData("character", "0")]
    [InlineData("character", "999")]
    public async Task CreateGoalUndefinedNumericEnumIsRejected(string entityType, string goalType)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostMutatedGoalAsync(RankGoal, request =>
        {
            request["entityType"] = entityType;
            request["goals"]![0]!["goalType"] = goalType;
        });

        await response.AssertFieldRejectedAsync(entityType == "character" ? "goalType" : "entityType");
        Assert.Empty((await client.ListGoalsAsync()).Goals);
    }

    [Fact]
    public async Task CreateGoalReadsEnumValuesCaseInsensitively()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostMutatedGoalAsync(RankGoal, request =>
        {
            request["entityType"] = "character";
            request["goals"]![0]!["goalType"] = "rank";
        });

        var goal = await response.ReadCreatedGoalAsync();
        Assert.Equal(GoalEntityType.Character, goal.EntityType);
        Assert.Equal(GoalType.Rank, goal.GoalType);
    }

    [Fact]
    public async Task CreateGoalRejectsAJsonNumberForAnEnum()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        // 0 is a defined member (Character): numbers are refused even when they name one.
        var response = await client.PostMutatedGoalAsync(RankGoal, request => request["entityType"] = 0);

        await response.AssertFieldRejectedAsync("entityType");
        Assert.Empty((await client.ListGoalsAsync()).Goals);
    }

    [Fact]
    public async Task CreateGoalUndefinedNumericFarmingStrategyIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostMutatedGoalAsync(
            RankGoal, request => request["goals"]![0]!["config"]!["farmingStrategy"] = "0");

        await response.AssertFieldRejectedAsync("farmingStrategy");
        Assert.Empty((await client.ListGoalsAsync()).Goals);
    }

    [Theory]
    [InlineData("upgrade", "rank")]
    [InlineData("character", "material")]
    public async Task CreateGoalDeferredGoalTypeOrEntityTypeIsRejected(string entityType, string goalType)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostMutatedGoalAsync(RankGoal, request =>
        {
            request["entityType"] = entityType;
            request["goals"]![0]!["goalType"] = goalType;
        });

        await response.AssertFieldRejectedAsync(entityType == "character" ? "goalType" : "entityType");
        Assert.Empty((await client.ListGoalsAsync()).Goals);
    }

    [Fact]
    public async Task CreateMowRankGoalIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(RankGoal with { EntityType = GoalEntityType.Mow, GoalType = GoalType.Rank });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateMowAbilityGoalIsAccepted()
    {
        // Machines of War have no rank ladder (plan §16 phase 6) — Ability is their natural goal type.
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Mow,
                "astraOrdnanceBattery",
                GoalType.Ability,
                new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 3, 0, 3)),
                null
            ));
        response.EnsureSuccessStatusCode();
        var created = await response.ReadCreatedGoalAsync();

        Assert.NotNull(created);
        Assert.Equal(GoalEntityType.Mow, created.EntityType);
        Assert.Equal(GoalType.Ability, created.GoalType);
    }

    [Fact]
    public async Task UpdateGoalChangesNotesAndFarmingLocationOverride()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var updateResponse = await client.PutDetailsAsync(created.GoalId, new UpdateGoalRequest("remember to farm epic first", [CampaignBattleId.From("DGS01")]));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.ReadEditedGoalAsync();

        Assert.NotNull(updated);
        Assert.Equal("remember to farm epic first", updated.Notes);
        Assert.Equal(["DGS01"], updated.Config.FarmingLocationIds);
        // The immutable target fields must be untouched by an edit.
        Assert.NotNull(created.Config.Rank);
        Assert.NotNull(updated.Config.Rank);
        Assert.Equal(created.Config.Rank.Start, updated.Config.Rank.Start);
        Assert.Equal(created.Config.Rank.End, updated.Config.Rank.End);
    }

    [Fact]
    public async Task UpdateGoalAddsAShopAcquisitionSource()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var createResponse = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "eldarFarseer",
                GoalType.Ascension,
                new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar)),
                null
            ));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.ReadCreatedGoalAsync();
        Assert.NotNull(created);
        Assert.Null(created.Config.AcquisitionSources);

        var updateResponse = await client.PutDetailsAsync(created.GoalId, new UpdateGoalRequest(
                null,
                null,
                AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, ["guild:shards_eldarFarseer"])]
            ));
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.ReadEditedGoalAsync();

        Assert.NotNull(updated);
        var source = Assert.Single(updated.Config.AcquisitionSources!);
        Assert.Equal(AcquisitionSourceKind.Shop, source.Kind);
        Assert.Equal(["guild:shards_eldarFarseer"], source.Ids);
    }

    [Fact]
    public async Task UpdateGoalOmittingAcquisitionSourcesRetainsTheExistingSelection()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var createResponse = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "eldarFarseer",
                GoalType.Ascension,
                new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar)),
                null
            ));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.ReadCreatedGoalAsync();
        Assert.NotNull(created);

        var withSource = await client.PutDetailsAsync(created.GoalId, new UpdateGoalRequest(
                null,
                null,
                AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, ["guild:shards_eldarFarseer"])]
            ));
        withSource.EnsureSuccessStatusCode();

        // A notes-only update — AcquisitionSources omitted (defaults to null) — must not wipe out the
        // selection set above (tacticus-planner-api CodeRabbit review of #44).
        var notesOnly = await client.PutDetailsAsync(created.GoalId, new UpdateGoalRequest("just a note", null));
        notesOnly.EnsureSuccessStatusCode();
        var updated = await notesOnly.ReadEditedGoalAsync();

        Assert.NotNull(updated);
        Assert.Equal("just a note", updated.Notes);
        var source = Assert.Single(updated.Config.AcquisitionSources!);
        Assert.Equal(AcquisitionSourceKind.Shop, source.Kind);
        Assert.Equal(["guild:shards_eldarFarseer"], source.Ids);
    }

    [Fact]
    public async Task UpdateGoalRejectsAnUnknownAcquisitionSourceKind()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var updateResponse = await client.SendMutatedJsonAsync(
            HttpMethod.Put,
            $"/api/v1/goals/{created.GoalId}",
            new EditGoalRequest(Details: new UpdateGoalRequest(
                null,
                null,
                AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Campaign, [])]
            )),
            request => request["details"]!["acquisitionSources"]![0]!["kind"] = "Auction");

        await updateResponse.AssertFieldRejectedAsync("kind");
        Assert.Equal(created.Revision, (await client.ListGoalsAsync()).Goals.Single(goal => goal.GoalId == created.GoalId).Revision);
    }

    [Fact]
    public async Task UpdateGoalStatusTransitionsAndBumpsTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var pauseResponse = await client.PostStatusAsync(created.GoalId, GoalStatus.Paused);
        pauseResponse.EnsureSuccessStatusCode();
        var paused = await pauseResponse.ReadStatusGoalAsync();

        Assert.NotNull(paused);
        Assert.Equal(GoalStatus.Paused, paused.Status);
        Assert.Equal(created.Revision + 1, paused.Revision);
    }

    [Fact]
    public async Task UpdateGoalStatusUnknownStatusIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var response = await client.SendMutatedJsonAsync(
            HttpMethod.Post,
            "/api/v1/goals/status",
            new UpdateGoalsStatusRequest([created.GoalId], GoalStatus.Paused, (await client.ListGoalsAsync()).OrderRevision),
            request => request["status"] = "not-a-status");

        await response.AssertFieldRejectedAsync("status");
        Assert.Equal(GoalStatus.Active, (await client.ListGoalsAsync()).Goals.Single(goal => goal.GoalId == created.GoalId).Status);
    }

    [Fact]
    public async Task UpdateGoalStatusUndefinedNumericStatusIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var response = await client.SendMutatedJsonAsync(
            HttpMethod.Post,
            "/api/v1/goals/status",
            new UpdateGoalsStatusRequest([created.GoalId], GoalStatus.Paused, (await client.ListGoalsAsync()).OrderRevision),
            request => request["status"] = "0");

        await response.AssertFieldRejectedAsync("status");
        Assert.Equal(GoalStatus.Active, (await client.ListGoalsAsync()).Goals.Single(goal => goal.GoalId == created.GoalId).Status);
    }

    [Fact]
    public async Task UpdateGoalRejectsUnsupportedFarmingStrategyForGoalType()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var createResponse = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "blackTerminator",
                GoalType.Ascension,
                new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar)),
                null
            ));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.ReadCreatedGoalAsync();
        Assert.NotNull(created);

        var response = await client.PutDetailsAsync(created.GoalId, new UpdateGoalRequest(null, null, FarmingStrategy.EveryStep));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListGoalsExcludesArchivedByDefaultButArchivedQueryReturnsThem()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var archiveResponse = await client.PostStatusAsync(created.GoalId, GoalStatus.Archived);
        archiveResponse.EnsureSuccessStatusCode();

        var defaultList = await client.GetFromJsonAsync<ListGoalsResponse>(
            "/api/v1/goals",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(defaultList);
        Assert.DoesNotContain(defaultList.Goals, goal => goal.GoalId == created.GoalId);

        var archivedList = await client.GetFromJsonAsync<ListGoalsResponse>(
            "/api/v1/goals?archived=true",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(archivedList);
        Assert.Contains(archivedList.Goals, goal => goal.GoalId == created.GoalId);
    }

    [Fact]
    public async Task DeleteGoalRemovesRowPermanently()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/goals", TestContext.Current.CancellationToken);
        var list = await listResponse.Content.ReadFromJsonAsync<ListGoalsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(list);
        Assert.DoesNotContain(list.Goals, goal => goal.GoalId == created.GoalId);

        var getResponse = await client.GetAsync(
            $"/api/v1/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // The row is gone (hard delete), so a second delete is a 404, not a no-op success.
        var secondDelete = await client.DeleteAsync(
            $"/api/v1/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    [Fact]
    public async Task GetGoalOwnedByAnotherProfileIsNotFound()
    {
        var ownerClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(ownerClient);

        var otherClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var response = await otherClient.GetAsync(
            $"/api/v1/goals/{created.GoalId}",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGoalProjectsAddsGoalToAnotherProject()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var created = await CreateGoalAsync(client); // starts in the default project only

        var otherProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(otherProject);

        var response = await client.PutProjectsAsync(created.GoalId, [defaultProject.ProjectId, otherProject.ProjectId]);
        response.EnsureSuccessStatusCode();
        var updated = await response.ReadEditedGoalAsync();

        Assert.NotNull(updated);
        Assert.Equal(
            new HashSet<Guid> { defaultProject.ProjectId, otherProject.ProjectId },
            updated.ProjectIds.ToHashSet()
        );

        var otherMembers = await client.GetProjectGoalsAsync(otherProject.ProjectId);
        Assert.NotNull(otherMembers);
        Assert.Contains(otherMembers.Goals, entry => entry.Goal.GoalId == created.GoalId);
    }

    [Fact]
    public async Task UpdateGoalProjectsRemovesGoalFromProjectKeepingItInAnother()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var otherProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(otherProject);

        var createResponse = await client.PostGoalAsync(RankGoal with
        {
            Projects =
                [
                    new ProjectPriorityRequest(defaultProject.ProjectId),
                    new ProjectPriorityRequest(otherProject.ProjectId),
                ],
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.ReadCreatedGoalAsync();
        Assert.NotNull(created);

        var remainingGoalResponse = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "blackTerminator",
                GoalType.Ascension,
                new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar)),
                null
            ));
        remainingGoalResponse.EnsureSuccessStatusCode();
        var remainingGoal = await remainingGoalResponse.ReadCreatedGoalAsync();
        Assert.NotNull(remainingGoal);

        var response = await client.PutProjectsAsync(created.GoalId, [otherProject.ProjectId]);
        response.EnsureSuccessStatusCode();
        var updated = await response.ReadEditedGoalAsync();

        Assert.NotNull(updated);
        Assert.Equal([otherProject.ProjectId], updated.ProjectIds);

        var defaultMembers = await client.GetProjectGoalsAsync(defaultProject.ProjectId);
        Assert.NotNull(defaultMembers);
        Assert.DoesNotContain(defaultMembers.Goals, entry => entry.Goal.GoalId == created.GoalId);
        // Membership changes never touch the account-wide order: the goals created first and second
        // keep positions 1 and 2 even though the first left the project.
        Assert.Equal(
            2,
            Assert.Single(defaultMembers.Goals, entry => entry.Goal.GoalId == remainingGoal.GoalId).Goal.GlobalPriority
        );
    }

    [Fact]
    public async Task UpdateGoalProjectsEmptyListIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var response = await client.PutProjectsAsync(created.GoalId, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGoalProjectsUnknownProjectIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);

        var response = await client.PutProjectsAsync(created.GoalId, [Guid.NewGuid()]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateGoalProjectsForAnotherProfilesGoalIsNotFound()
    {
        var ownerClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(ownerClient);

        var otherClient = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var otherDefaultProject = await GetDefaultProjectAsync(otherClient);

        var response = await otherClient.PutProjectsAsync(created.GoalId, [otherDefaultProject.ProjectId]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalConflictingWithActiveSameKindGoalIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await CreateGoalAsync(client); // starts Active in the default/active project

        var response = await client.PostGoalAsync(RankGoal);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalConflictingWithPausedSameKindGoalIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);
        var pauseResponse = await client.PostStatusAsync(created.GoalId, GoalStatus.Paused);
        pauseResponse.EnsureSuccessStatusCode();

        var response = await client.PostGoalAsync(RankGoal);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateGoalAllowedWhenSameKindGoalIsCompletedOrArchived()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client);
        var completeResponse = await client.PostStatusAsync(created.GoalId, GoalStatus.Completed);
        completeResponse.EnsureSuccessStatusCode();

        var response = await client.PostGoalAsync(RankGoal);

        response.EnsureSuccessStatusCode();
        var created2 = await response.ReadCreatedGoalAsync();
        Assert.NotNull(created2);
        Assert.NotEqual(created.GoalId, created2.GoalId);
    }

    [Fact]
    public async Task ResumingGoalConflictingWithAnotherActiveSameKindGoalIsRejected()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var first = await CreateGoalAsync(client); // Active
        var pauseResponse = await client.PostStatusAsync(first.GoalId, GoalStatus.Archived);
        pauseResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostGoalAsync(RankGoal);
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.ReadCreatedGoalAsync();
        Assert.NotNull(second);

        // Un-archiving the first goal back to Active would exceed the one-active-or-paused-per-kind
        // limit now that the second goal already occupies that slot.
        var resumeResponse = await client.PostStatusAsync(first.GoalId, GoalStatus.Active);

        Assert.Equal(HttpStatusCode.Conflict, resumeResponse.StatusCode);
    }

    [Fact]
    public async Task PausingAlreadyActiveGoalDoesNotConflictWithItself()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var created = await CreateGoalAsync(client); // Active

        var response = await client.PostStatusAsync(created.GoalId, GoalStatus.Paused);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ASecondProjectSharesTheInFlightGoalInsteadOfHoldingItsOwn()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var defaultProject = await GetDefaultProjectAsync(client);
        var projectResponse = await client.PostAsJsonAsync(
            "/api/v1/projects",
            new CreateProjectRequest("Second plan", null, null),
            TestContext.Current.CancellationToken);
        projectResponse.EnsureSuccessStatusCode();
        var secondProject = await projectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(secondProject);

        var first = await CreateGoalAsync(client);
        var secondResponse = await client.PostGoalAsync(RankGoal with { Projects = [new ProjectPriorityRequest(secondProject.ProjectId)] });

        // Slots are per profile: the second project gets the existing goal, not a second instance.
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal(first.GoalId, (await secondResponse.ReadProblemAsync())["existingGoalId"]!.GetValue<Guid>());
        var shared = await client.PutProjectsAsync(first.GoalId, [defaultProject.ProjectId, secondProject.ProjectId]);
        Assert.Equal(
            new[] { defaultProject.ProjectId, secondProject.ProjectId }.Order(),
            (await shared.ReadEditedGoalAsync()).ProjectIds);
    }

    [Fact]
    public async Task ResumeIsRejectedAtomicallyWhenAnotherGoalTookTheSlot()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var firstProject = await GetDefaultProjectAsync(client);
        var secondProjectResponse = await client.PostAsJsonAsync(
            "/api/v1/projects", new CreateProjectRequest("Second plan", null, null),
            TestContext.Current.CancellationToken);
        var secondProject = await secondProjectResponse.Content.ReadFromJsonAsync<ProjectSummaryResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(secondProject);

        var sharedResponse = await client.PostGoalAsync(RankGoal with
        {
            Projects = [
                new ProjectPriorityRequest(firstProject.ProjectId),
                new ProjectPriorityRequest(secondProject.ProjectId),
            ]
        });
        sharedResponse.EnsureSuccessStatusCode();
        var shared = await sharedResponse.ReadCreatedGoalAsync();
        Assert.NotNull(shared);
        var archive = await client.PostStatusAsync(shared.GoalId, GoalStatus.Archived);
        archive.EnsureSuccessStatusCode();

        var competing = await client.PostGoalAsync(RankGoal with { Projects = [new ProjectPriorityRequest(secondProject.ProjectId)] });
        competing.EnsureSuccessStatusCode();

        var resume = await client.PostStatusAsync(shared.GoalId, GoalStatus.Active);
        Assert.Equal(HttpStatusCode.Conflict, resume.StatusCode);
        var conflict = await resume.ReadProblemAsync();
        Assert.Equal("slotOccupied", conflict["issueCode"]!.GetValue<string>());
        Assert.Equal(shared.GoalId, conflict["goalId"]!.GetValue<Guid>());
        Assert.Equal((await competing.ReadCreatedGoalAsync()).GoalId, conflict["existingGoalId"]!.GetValue<Guid>());

        var unchanged = await client.GetFromJsonAsync<GoalDetailResponse>(
            $"/api/v1/goals/{shared.GoalId}", TestContext.Current.CancellationToken);
        Assert.Equal(GoalStatus.Archived, unchanged?.Status);
    }

    [Fact]
    public async Task CreateRankGoalRoundTripsMythicMaterialShopSourcesAndFarmingLocations()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(RankGoal with
        {
            Config = RankGoal.Config with
            {
                FarmingLocationIds = [CampaignBattleId.From("DGS01")],
                AcquisitionSources = [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, ["guild:upgHpM004", "crusade:upgHpM004"])],
            },
        });
        response.EnsureSuccessStatusCode();
        var created = await response.ReadCreatedGoalAsync();

        Assert.NotNull(created);
        Assert.Equal(["DGS01"], created.Config.FarmingLocationIds);
        var source = Assert.Single(created.Config.AcquisitionSources!);
        Assert.Equal(AcquisitionSourceKind.Shop, source.Kind);
        Assert.Equal(["guild:upgHpM004", "crusade:upgHpM004"], source.Ids);
    }

    [Fact]
    public async Task CreateRankGoalKeepsAnExplicitEmptyShopSourceDistinctFromNull()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(RankGoal with { Config = RankGoal.Config with { AcquisitionSources = [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, [])] } });
        response.EnsureSuccessStatusCode();
        var created = await response.ReadCreatedGoalAsync();

        // Not collapsed to null (the "every available offer" default): an explicit opt-out survives.
        Assert.NotNull(created);
        var source = Assert.Single(created.Config.AcquisitionSources!);
        Assert.Equal(AcquisitionSourceKind.Shop, source.Kind);
        Assert.Empty(source.Ids);
    }

    [Theory]
    [InlineData("guild:shards_blackTerminator")]
    [InlineData("guild:upgDmgL202")]
    public async Task CreateRankGoalRejectsNonMythicMaterialShopOffers(string offerId)
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(RankGoal with { Config = RankGoal.Config with { AcquisitionSources = [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, [offerId])] } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAscensionGoalRejectsAMythicMaterialShopOffer()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostGoalAsync(new CreateGoalRequest(
                GoalEntityType.Character,
                "eldarFarseer",
                GoalType.Ascension,
                new CreateGoalConfigRequest(
                    Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonOneStar),
                    AcquisitionSources: [new AcquisitionSourceRequest(AcquisitionSourceKind.Shop, ["guild:upgHpM004"])]
                ),
                null
            ));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<GoalDetailResponse> CreateGoalAsync(HttpClient client)
    {
        var response = await client.PostGoalAsync(RankGoal);
        response.EnsureSuccessStatusCode();
        var goal = await response.ReadCreatedGoalAsync();
        Assert.NotNull(goal);
        return goal;
    }

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/projects",
            TestContext.Current.CancellationToken
        );
        Assert.NotNull(response);
        return response.Projects.Single(project => project.IsDefault);
    }
}
