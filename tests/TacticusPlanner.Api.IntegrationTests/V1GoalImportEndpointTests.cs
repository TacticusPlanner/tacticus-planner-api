using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>
/// Covers `rewrite-v1-goal-import`'s `v1-goal-import` capability: server-side goal creation (no
/// returned specs), the goals-refused-without-player-data rule, per-source-goal outcomes and their
/// skip/not-imported/failed classification, V1 shard-source translation, V1 notes, automatic
/// prerequisite synthesis, ordering, and idempotency. Uses <see cref="factory.V1Client.ConfigureProfile"/>
/// so each test supplies its own V1 goal list without touching the shared fixed fixtures.
/// </summary>
public sealed class V1GoalImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string CharacterId = "ultraInceptorSgt"; // catalog Name "Bellator" (see TacticusTestDoubles.cs)
    private const string OtherCharacterId = "blackTerminator";
    private const string MowId = "astraOrdnanceBattery";
    private const string UltraStoneUpgradeId = "upgHpC001"; // on ultraInceptorSgt's Stone1 rank-up row
    private const string SharedStoneUpgradeId = "upgArmU002"; // once on both units' Stone1 -> Iron1 rank-ups
    private static readonly string[] PreFarmUnits = [CharacterId, OtherCharacterId];
    private static readonly string[] PreFarmLinks = ["r1", "r2"];
    private static readonly (string, int)[] MergedTargets = [(SharedStoneUpgradeId, 1), (UltraStoneUpgradeId, 10)];

    private static readonly string[] RarityNames = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];
    private static readonly string[] StarNames =
    [
        "None", "OneStar", "TwoStars", "ThreeStars", "FourStars", "FiveStars", "RedOneStar",
        "RedTwoStars", "RedThreeStars", "RedFourStars", "RedFiveStars", "OneBlueStar",
        "TwoBlueStars", "ThreeBlueStars", "MythicWings",
    ];

    // ----- Refusal without player data -----

    [Fact]
    public async Task ImportRollsBackGoalsMembershipsAndOrderAfterSqlWasWritten()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);
        var username = factory.V1Client.ConfigureProfile(new TacticusV1Profile(
            null, null, null, [RankGoal("rollback", CharacterId, 1, UnitRank.Iron1)],
            V1OnslaughtImportData.Missing(), V1CampaignEventProgressImportData.Missing()));
        factory.FailureInjected = false;
        factory.FailAfterSave = db => db.ChangeTracker.Entries<TacticusPlanner.Domain.Goals.Goal>().Any();
        try
        {
            var response = await client.PostAsJsonAsync("/api/v1/account/v1-import", new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(false, false, false, true, false, false)), ct);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.True(factory.FailureInjected);
            var goals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/goals", ct);
            Assert.Empty(goals!.Goals);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var profileId = await db.Profiles.IgnoreQueryFilters().Where(profile => profile.Subject == subject).Select(profile => profile.Id).SingleAsync(ct);
            Assert.False(await db.ProjectGoals.IgnoreQueryFilters().AnyAsync(entry => entry.Project!.ProfileId == profileId, ct));
            Assert.Equal(0, await db.Profiles.IgnoreQueryFilters().Where(profile => profile.Id == profileId).Select(profile => profile.GoalOrderRevision).SingleAsync(ct));
        }
        finally
        {
            factory.FailAfterSave = null;
        }
    }

    [Fact]
    public async Task GoalsAreRefusedWithoutPlayerDataAndNoGoalsAreCreated()
    {
        var (client, _) = await CreateProvisionedClientAsync();

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1)]);

        Assert.Equal(ImportPartStatus.Failed, body.Goals.Status);
        Assert.Equal("player_data_required", body.Goals.Code);
        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Failed, outcome.Status);
        Assert.Equal("player_data_required", outcome.Code);

        var goals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/goals", TestContext.Current.CancellationToken);
        Assert.Empty(goals!.Goals);
    }

    [Fact]
    public async Task GoalsSyncAndImportSucceedOnTheFirstRequestWhenAValidKeyIsAvailable()
    {
        // A selected personal key gives EnsurePlayerDataSyncedAsync something to sync with before goals
        // run, so — unlike GoalsAreRefusedWithoutPlayerDataAndNoGoalsAreCreated above, where no key is
        // available anywhere — this first request no longer needs a second, manually-seeded run.
        var (client, subject) = await CreateProvisionedClientAsync();
        var username = factory.V1Client.ConfigureProfile(new TacticusV1Profile(
            FakeTacticusApi.ValidKey, $"user-{Guid.NewGuid()}", null, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1)],
            V1OnslaughtImportData.Missing(), V1CampaignEventProgressImportData.Missing()));

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(true, true, false, true, false, false)),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(ImportPartStatus.Imported, body!.PersonalTacticusApiKey.Status);
        Assert.Equal(ImportPartStatus.Imported, body.TacticusUserId.Status);
        Assert.Equal(ImportPartStatus.Imported, body.Goals.Status);
        // The character starts unowned in the synced snapshot, so the Rank goal also pulls in its
        // Unlock/Level prerequisites automatically — all three still land as "Created", not refused.
        Assert.All(body.Outcomes, outcome => Assert.Equal(V1GoalOutcomeStatus.Created, outcome.Status));
        Assert.Contains(body.Outcomes, outcome => outcome.SourceGoalId == "r1");
        _ = subject;
    }

    [Fact]
    public async Task OtherSelectedPartsStillProcessWhenGoalsAreRefusedForLackOfAKeyToSyncWith()
    {
        var (client, _) = await CreateProvisionedClientAsync();
        var username = factory.V1Client.ConfigureProfile(new TacticusV1Profile(
            null, $"user-{Guid.NewGuid()}", null, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1)],
            V1OnslaughtImportData.Missing(), V1CampaignEventProgressImportData.Missing()));

        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(false, true, false, true, false, false)),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(ImportPartStatus.Failed, body!.Goals.Status);
        Assert.Equal("player_data_required", body.Goals.Code);
        Assert.Equal(ImportPartStatus.Imported, body.TacticusUserId.Status);
    }

    [Fact]
    public async Task ImportingAfterASyncSucceeds()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        var refused = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1)]);
        Assert.True(refused.Outcomes[0].Code == "player_data_required");

        // xpLevel 60: Iron1's own required level (8) is already met, so only the Rank goal is created.
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);
        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Created, outcome.Status);
    }

    // ----- Outcome shape / classification -----

    [Fact]
    public async Task OutcomeCountMatchesTheSourceProfile()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var goals = Enumerable.Range(0, 5)
            .Select(i => UnsupportedGoal($"u{i}", i))
            .ToList();
        var body = await ImportGoalsAsync(client, goals, automaticPrerequisites: false);

        Assert.Equal(5, body.Outcomes.Count);
    }

    [Fact]
    public async Task SeveralGoalsForOneUnitAreReportedIndividually()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1), AscensionGoal("a1", CharacterId, 2, UnitProgression.CommonTwoStars)],
            automaticPrerequisites: false);

        Assert.Equal(2, body.Outcomes.Count);
        Assert.All(body.Outcomes, outcome => Assert.Equal(V1GoalOutcomeStatus.Created, outcome.Status));
        Assert.Contains(body.Outcomes, o => o.GoalType == GoalType.Rank);
        Assert.Contains(body.Outcomes, o => o.GoalType == GoalType.Ascension);
        Assert.Equal(2, body.Outcomes.Select(o => o.GoalId).Distinct().Count());
    }

    [Fact]
    public async Task OutcomesFollowV1PriorityOrder()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60), Character("blackTerminator", xpLevel: 60)]);

        // Submitted out of priority order; the response must still be ordered by priority (3, then 7).
        var body = await ImportGoalsAsync(
            client,
            [
                RankGoal("high-priority", "blackTerminator", 7, UnitRank.Iron1),
                RankGoal("low-priority", CharacterId, 3, UnitRank.Iron1),
            ],
            automaticPrerequisites: false);

        Assert.Equal(["low-priority", "high-priority"], body.Outcomes.Select(o => o.SourceGoalId).ToList());
    }

    [Fact]
    public async Task AlreadyReachedTargetIsSkipped()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, rank: UnitRank.Gold1)]);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Skipped, outcome.Status);
        Assert.Equal("target_already_reached", outcome.Code);
    }

    [Fact]
    public async Task AlreadyExistingGoalIsSkippedAndCarriesTheExistingGoalId()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);
        var existing = await CreateNativeGoalAsync(client, CharacterId, GoalType.Ascension,
            new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.CommonTwoStars)));

        var body = await ImportGoalsAsync(client, [AscensionGoal("a1", CharacterId, 1, UnitProgression.CommonOneStar)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Skipped, outcome.Status);
        Assert.Equal("goal_already_exists", outcome.Code);
        Assert.Equal(existing.GoalId, outcome.GoalId);
    }

    [Fact]
    public async Task DuplicateRankGoalsWithTheSameEndMergeIntoOneGoalAndCombineNotes()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client,
            [
                RankGoal("r-first", CharacterId, 1, UnitRank.Bronze1, notes: "first"),
                RankGoal("r-second", CharacterId, 2, UnitRank.Bronze1, notes: "second"),
            ],
            automaticPrerequisites: false);

        var created = Assert.Single(body.Outcomes, o => o.Status == V1GoalOutcomeStatus.Created);
        var merged = Assert.Single(body.Outcomes, o => o.Status == V1GoalOutcomeStatus.Skipped);
        Assert.Equal("duplicate_goal_merged", merged.Code);
        Assert.Equal(created.GoalId, merged.GoalId);
        Assert.Equal("r-first", created.SourceGoalId); // higher-priority (earlier) source keeps the slot
        Assert.Equal("r-second", merged.SourceGoalId);

        var goal = await GetGoalAsync(client, created.GoalId!.Value);
        Assert.Equal((int)UnitRank.Bronze1, goal.Config.Rank!.End);
        Assert.Contains("first", goal.Notes);
        Assert.Contains("second", goal.Notes);
    }

    [Fact]
    public async Task DistinctRankTargetsImportAsSeparateMilestonesWithTheirOwnNotesInV1Order()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client,
            [
                RankGoal("r-low", CharacterId, 1, UnitRank.Bronze1, notes: "first"),
                RankGoal("r-high", CharacterId, 2, UnitRank.Silver1, notes: "second"),
            ],
            automaticPrerequisites: false);

        Assert.All(body.Outcomes, outcome => Assert.Equal(V1GoalOutcomeStatus.Created, outcome.Status));
        var low = await GetGoalAsync(client, body.Outcomes[0].GoalId!.Value);
        var high = await GetGoalAsync(client, body.Outcomes[1].GoalId!.Value);
        Assert.NotEqual(low.GoalId, high.GoalId);
        Assert.Equal((int)UnitRank.Bronze1, low.Config.Rank!.End);
        Assert.Equal((int)UnitRank.Silver1, high.Config.Rank!.End);
        Assert.Equal("first", low.Notes);
        Assert.Equal("second", high.Notes);

        var listed = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/goals", TestContext.Current.CancellationToken);
        Assert.Equal(2, listed!.Goals.Count);
    }

    [Fact]
    public async Task DuplicateRankGoalsWithEquivalentEndsMergeAndKeepThePointFiveRepresentation()
    {
        // Point-five below Adamantine1 is the same end state as three applied slots, so the two goals are
        // exact duplicates (same normalized key), not distinct milestones.
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);
        var target = (int)UnitRank.Bronze1 + 1;

        var body = await ImportGoalsAsync(
            client,
            [
                new V1Goal("r-three-slots", CharacterId, 1, 1, true, null, null, null, null, target, false, 3,
                    null, null, null, null, null, null, null),
                new V1Goal("r-pointfive", CharacterId, 1, 2, true, null, null, null, null, target, true, 0,
                    null, null, null, null, null, null, null),
            ],
            automaticPrerequisites: false);

        var created = Assert.Single(body.Outcomes, o => o.Status == V1GoalOutcomeStatus.Created);
        Assert.Equal("duplicate_goal_merged", Assert.Single(body.Outcomes, o => o.Status == V1GoalOutcomeStatus.Skipped).Code);
        var goal = await GetGoalAsync(client, created.GoalId!.Value);
        Assert.True(goal.Config.Rank!.EndPointFive);
    }

    [Fact]
    public async Task ReimportingAHigherRankTargetCreatesItBesideTheExistingMilestone()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var first = await ImportGoalsAsync(
            client, [RankGoal("r-low", CharacterId, 1, UnitRank.Bronze1)], automaticPrerequisites: false);
        var second = await ImportGoalsAsync(
            client,
            [RankGoal("r-low", CharacterId, 1, UnitRank.Bronze1), RankGoal("r-high", CharacterId, 2, UnitRank.Silver1)],
            automaticPrerequisites: false);

        Assert.Equal(V1GoalOutcomeStatus.Created, first.Outcomes[0].Status);
        Assert.Equal("goal_already_exists", second.Outcomes[0].Code);
        Assert.Equal(first.Outcomes[0].GoalId, second.Outcomes[0].GoalId);
        Assert.Equal(V1GoalOutcomeStatus.Created, second.Outcomes[1].Status);
        Assert.NotEqual(first.Outcomes[0].GoalId, second.Outcomes[1].GoalId);
    }

    [Fact]
    public async Task DuplicateAscensionGoalsUnionTheirAcquisitionSources()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client,
            [
                AscensionGoal("a-campaign", CharacterId, 1, UnitProgression.CommonTwoStars, campaignsUsage: 1),
                AscensionGoal("a-onslaught", CharacterId, 2, UnitProgression.UncommonTwoStars, shardFarmType: V1ShardFarmType.Onslaught),
            ],
            automaticPrerequisites: false);

        var created = Assert.Single(body.Outcomes, o => o.Status == V1GoalOutcomeStatus.Created);
        var goal = await GetGoalAsync(client, created.GoalId!.Value);
        var kinds = goal.Config.AcquisitionSources!.Select(source => source.Kind).ToHashSet();
        Assert.Contains(AcquisitionSourceKind.Campaign, kinds);
        Assert.Contains(AcquisitionSourceKind.Onslaught, kinds);
    }

    [Fact]
    public async Task AnExistingAscensionGoalThatAlreadyReachesTheNeededTargetReportsNoShortfall()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);
        await CreateNativeGoalAsync(client, CharacterId, GoalType.Ascension,
            new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(UnitProgression.CommonNone, UnitProgression.MythicMythicWings)));

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Gold1)]);

        Assert.DoesNotContain(body.Outcomes, o => o.Code == "prerequisite_target_insufficient");
        Assert.DoesNotContain(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == GoalType.Ascension);
        var rankOutcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "r1");
        Assert.Equal(V1GoalOutcomeStatus.Created, rankOutcome.Status);
    }

    [Fact]
    public async Task DuplicateAbilityGoalsKeepTheHigherPriorityOne()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var body = await ImportGoalsAsync(
            client,
            [AbilityGoal("ab-low", CharacterId, 1, 5, 0), AbilityGoal("ab-high", CharacterId, 2, 7, 0)],
            automaticPrerequisites: false);

        var created = Assert.Single(body.Outcomes, o => o.Status == V1GoalOutcomeStatus.Created);
        var merged = Assert.Single(body.Outcomes, o => o.Status == V1GoalOutcomeStatus.Skipped);
        Assert.Equal("ab-low", created.SourceGoalId);
        Assert.Equal("duplicate_goal_merged", merged.Code);
    }

    [Fact]
    public async Task UnsupportedV1GoalTypeIsNotImportedAndPromisesNoFutureSupport()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var body = await ImportGoalsAsync(client, [UnsupportedGoal("u1", 1)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Failed, outcome.Status);
        Assert.Equal("unsupported_goal_type", outcome.Code);
        Assert.DoesNotContain("future", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("soon", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("will be", outcome.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownUnitIsNotImportedAndCarriesTheRawV1Identifier()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", "totally-unknown-unit-xyz", 1, UnitRank.Iron1)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Failed, outcome.Status);
        Assert.Equal("unknown_unit", outcome.Code);
        Assert.Equal("totally-unknown-unit-xyz", outcome.EntityId);
    }

    [Fact]
    public async Task InvalidProgressionTargetIsNotImported()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        // Common rarity paired with the MythicWings star count is not a real ladder step.
        var body = await ImportGoalsAsync(client, [new V1Goal(
            "a1", CharacterId, 2, 1, true, null, null, null, null, null, null, null,
            null, null, 0, 14, null, null, null)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Failed, outcome.Status);
        Assert.Equal("invalid_progression", outcome.Code);
    }

    [Fact]
    public async Task MissingTargetIsNotImported()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var body = await ImportGoalsAsync(client, [new V1Goal(
            "r1", CharacterId, 1, 1, true, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Failed, outcome.Status);
        Assert.Equal("missing_target", outcome.Code);
    }

    [Fact]
    public async Task OneRejectedTargetDoesNotDiscardTheOthers()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        // Common rarity -> ability cap 8. Without prerequisite synthesis, an ability target above it is
        // genuinely rejected by GoalTargetValidationService — a real creation-time failure, not a
        // translate-time skip.
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId), Character("blackTerminator")]);

        var body = await ImportGoalsAsync(
            client,
            [
                RankGoal("valid", CharacterId, 1, UnitRank.Iron1),
                AbilityGoal("over-cap", "blackTerminator", 2, 50, 0),
            ],
            automaticPrerequisites: false);

        var valid = Assert.Single(body.Outcomes, o => o.SourceGoalId == "valid");
        var rejected = Assert.Single(body.Outcomes, o => o.SourceGoalId == "over-cap");
        Assert.Equal(V1GoalOutcomeStatus.Created, valid.Status);
        Assert.Equal(V1GoalOutcomeStatus.Failed, rejected.Status);
        Assert.Equal("target_rejected", rejected.Code);

        var goals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/goals", TestContext.Current.CancellationToken);
        Assert.Single(goals!.Goals);
    }

    [Fact]
    public async Task CreatedGoalsCarryASnapshotBuiltFromPlayerData()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, UnitProgression.RareFourStars, UnitRank.Silver2, 40, 12, 9)]);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Gold1)], automaticPrerequisites: false);
        var goal = await GetGoalAsync(client, body.Outcomes[0].GoalId!.Value);

        Assert.Equal(UnitRank.Silver2, goal.Snapshot!.InitialRank);
        Assert.Equal(UnitProgression.RareFourStars, goal.Snapshot.InitialProgression);
        Assert.Equal(12, goal.Snapshot.InitialActiveAbilityLevel);
        Assert.Equal(9, goal.Snapshot.InitialPassiveAbilityLevel);
    }

    [Fact]
    public async Task V1NotesSurviveTheImport()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1, notes: "farm at night")], automaticPrerequisites: false);
        var goal = await GetGoalAsync(client, body.Outcomes[0].GoalId!.Value);

        Assert.Equal("farm at night", goal.Notes);
    }

    // ----- V1 shard-source translation -----

    [Fact]
    public async Task OnslaughtOnlyAscensionKeepsOnslaughtAndNoCampaignSource()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var body = await ImportGoalsAsync(
            client,
            [AscensionGoal("a1", CharacterId, 1, UnitProgression.CommonTwoStars, shardFarmType: V1ShardFarmType.Onslaught)],
            automaticPrerequisites: false);
        var goal = await GetGoalAsync(client, body.Outcomes[0].GoalId!.Value);

        var sources = goal.Config.AcquisitionSources!;
        Assert.Contains(sources, s => s.Kind == AcquisitionSourceKind.Onslaught);
        Assert.DoesNotContain(sources, s => s.Kind == AcquisitionSourceKind.Campaign);
    }

    [Fact]
    public async Task CombinedFarmingImportsBothSources()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var body = await ImportGoalsAsync(
            client,
            [AscensionGoal("a1", CharacterId, 1, UnitProgression.CommonTwoStars, shardFarmType: V1ShardFarmType.Both, campaignsUsage: 1)],
            automaticPrerequisites: false);
        var goal = await GetGoalAsync(client, body.Outcomes[0].GoalId!.Value);

        var sources = goal.Config.AcquisitionSources!;
        Assert.Contains(sources, s => s.Kind == AcquisitionSourceKind.Onslaught);
        Assert.Contains(sources, s => s.Kind == AcquisitionSourceKind.Campaign);
    }

    [Fact]
    public async Task NoCampaignUsageOmitsTheCampaignSource()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var body = await ImportGoalsAsync(
            client,
            [AscensionGoal("a1", CharacterId, 1, UnitProgression.CommonTwoStars, shardFarmType: V1ShardFarmType.Energy, campaignsUsage: 0)],
            automaticPrerequisites: false);
        var goal = await GetGoalAsync(client, body.Outcomes[0].GoalId!.Value);

        Assert.Null(goal.Config.AcquisitionSources);
    }

    [Fact]
    public async Task MachineOfWarAscensionOmitsTheInvalidOnslaughtSourceAndIsStillCreated()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, mows: [Mow(MowId)]);

        var body = await ImportGoalsAsync(
            client,
            [new V1Goal(
                "a1", null, 2, 1, true, null, null, null, null, null, null, null,
                null, null, 0, 2, MowId, null, null, V1ShardFarmType.Onslaught, 1, null)],
            automaticPrerequisites: false);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Created, outcome.Status);
        var goal = await GetGoalAsync(client, outcome.GoalId!.Value);
        Assert.DoesNotContain(goal.Config.AcquisitionSources ?? [], s => s.Kind == AcquisitionSourceKind.Onslaught);
        Assert.Contains(goal.Config.AcquisitionSources ?? [], s => s.Kind == AcquisitionSourceKind.Campaign);
    }

    [Fact]
    public async Task MachineOfWarAscensionStoredUnderCharacterImportsAsAMachineOfWarGoal()
    {
        // V1's goal dialog stores every unit under `character`, Mows included — no `unitId`.
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, mows: [Mow(MowId)]);

        var body = await ImportGoalsAsync(
            client, [AscensionGoal("a1", MowId, 1, UnitProgression.CommonTwoStars)], automaticPrerequisites: false);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Created, outcome.Status);
        Assert.Equal(GoalEntityType.Mow, outcome.EntityType);
        Assert.Equal(MowId, outcome.EntityId);
        Assert.Equal(GoalType.Ascension, outcome.GoalType);
    }

    // ----- V1 material goals (types 6 and 7) -----

    [Fact]
    public async Task UpgradeMaterialGoalBecomesAnUpgradeGoalOnTheSuggestedUnitAtItsOwnPriority()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(OtherCharacterId, xpLevel: 60), Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client,
            [
                RankGoal("r1", CharacterId, 1, UnitRank.Iron1),
                UpgradeMaterialGoal("m1", 2, UltraStoneUpgradeId, 10),
                RankGoal("r2", OtherCharacterId, 3, UnitRank.Iron1),
            ]);

        var outcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "m1");
        Assert.Equal(V1GoalOutcomeStatus.Created, outcome.Status);
        Assert.Equal("upgrade_goal_assigned", outcome.Code);
        Assert.Equal(GoalType.Upgrade, outcome.GoalType);
        Assert.Equal(CharacterId, outcome.EntityId);
        Assert.Equal(UltraStoneUpgradeId, outcome.UpgradeId);
        Assert.Equal(10, outcome.UpgradeQuantity);
        var goal = await GetGoalAsync(client, outcome.GoalId!.Value);
        var target = Assert.Single(goal.Config.Upgrade!.Targets);
        Assert.Equal((UltraStoneUpgradeId, 10), (target.UpgradeId, target.Quantity));

        var priorities = await ImportPrioritiesAsync(client);
        Assert.True(priorities[GoalIdOf(body, "r1")] < priorities[outcome.GoalId!.Value]);
        Assert.True(priorities[outcome.GoalId!.Value] < priorities[GoalIdOf(body, "r2")]);
    }

    [Fact]
    public async Task UpgradeMaterialGoalWithAnUnknownMaterialIsNotImported()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var body = await ImportGoalsAsync(client, [UpgradeMaterialGoal("m1", 1, "upgNotARealMaterial", 10)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Failed, outcome.Status);
        Assert.Equal("unknown_upgrade", outcome.Code);
        Assert.Equal("upgNotARealMaterial", outcome.UpgradeId);
    }

    [Fact]
    public async Task UpgradeMaterialGoalWithNoUnitNeedingTheMaterialIsNotImported()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject);

        var body = await ImportGoalsAsync(client, [UpgradeMaterialGoal("m1", 1, UltraStoneUpgradeId, 10)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Failed, outcome.Status);
        Assert.Equal("no_upgrade_owner", outcome.Code);
    }

    [Fact]
    public async Task PreFarmGoalBecomesAnUpgradeGoalPerLinkedUnitStartingAtItsPriority()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60), Character(OtherCharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client,
            [
                PreFarmGoal("p1", 1, SharedStoneUpgradeId, "r1", "r2", "gone"),
                RankGoal("r1", CharacterId, 2, UnitRank.Iron1),
                RankGoal("r2", OtherCharacterId, 3, UnitRank.Iron1),
            ]);

        var preFarm = body.Outcomes.Where(o => o.SourceGoalId == "p1").ToList();
        Assert.Equal(PreFarmUnits, preFarm.Select(o => o.EntityId));
        Assert.All(preFarm, outcome =>
        {
            Assert.Equal(V1GoalOutcomeStatus.Created, outcome.Status);
            Assert.Equal("goal_created", outcome.Code);
            Assert.Equal(GoalType.Upgrade, outcome.GoalType);
            Assert.Equal(SharedStoneUpgradeId, outcome.UpgradeId);
            Assert.Equal(PreFarmLinks, outcome.LinkedSourceGoalIds!);
            // Stone1 -> Iron1 on both units' ladders uses one.
            Assert.Equal(1, outcome.UpgradeQuantity);
        });

        var priorities = await ImportPrioritiesAsync(client);
        var ordered = new[] { preFarm[0].GoalId!.Value, preFarm[1].GoalId!.Value, GoalIdOf(body, "r1"), GoalIdOf(body, "r2") }
            .Select(id => priorities[id])
            .ToList();
        Assert.Equal(ordered.Order(), ordered);
    }

    [Fact]
    public async Task MaterialGoalsOnTheSameUnitMergeIntoOneUpgradeGoalAtTheEarliestPriority()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client,
            [
                PreFarmGoal("p1", 1, SharedStoneUpgradeId, "r1"),
                RankGoal("r1", CharacterId, 2, UnitRank.Iron1),
                UpgradeMaterialGoal("m1", 3, UltraStoneUpgradeId, 10),
            ]);

        var survivor = Assert.Single(body.Outcomes, o => o.SourceGoalId == "p1");
        var merged = Assert.Single(body.Outcomes, o => o.SourceGoalId == "m1");
        Assert.Equal("duplicate_goal_merged", merged.Code);
        Assert.Equal(survivor.GoalId, merged.GoalId);
        Assert.Equal(UltraStoneUpgradeId, merged.UpgradeId);
        var goal = await GetGoalAsync(client, survivor.GoalId!.Value);
        Assert.Equal(
            MergedTargets,
            goal.Config.Upgrade!.Targets.Select(target => (target.UpgradeId, target.Quantity)).Order());
    }

    [Fact]
    public async Task PreFarmGoalWhoseLinkedGoalsWerentImportedReportsAManualStep()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(client, [PreFarmGoal("p1", 1, UltraStoneUpgradeId, "gone")]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Skipped, outcome.Status);
        Assert.Equal("manual_step_required", outcome.Code);
        Assert.Equal(UltraStoneUpgradeId, outcome.UpgradeId);
        Assert.Equal(CharacterId, outcome.EntityId);
        Assert.Empty(outcome.LinkedSourceGoalIds!);
        Assert.Null(outcome.GoalId);
    }

    [Fact]
    public async Task AMaterialForAUnitWithAnotherUpgradeGoalReportsAManualStep()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);
        await CreateNativeGoalAsync(client, CharacterId, GoalType.Upgrade, new CreateGoalConfigRequest(
            Upgrade: new UpgradeTargetRequest([new UpgradeMaterialTargetRequest(SharedStoneUpgradeId, 1)])));

        var body = await ImportGoalsAsync(client, [UpgradeMaterialGoal("m1", 1, UltraStoneUpgradeId, 10)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal("manual_step_required", outcome.Code);
        Assert.Equal(CharacterId, outcome.EntityId);
        Assert.Equal(10, outcome.UpgradeQuantity);
    }

    // ----- Automatic prerequisite synthesis -----

    [Fact]
    public async Task UnlockIsCreatedForAUnitNotInTheRosterAndWiredAsADependency()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []); // account has player data, but not this unit

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2)]);

        var unlock = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == GoalType.Unlock);
        var rankOutcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "r1");
        Assert.Equal(V1GoalOutcomeStatus.Created, rankOutcome.Status);
        var rankGoal = await GetGoalAsync(client, rankOutcome.GoalId!.Value);
        Assert.Contains(unlock.GoalId!.Value, rankGoal.DependsOn);
    }

    [Fact]
    public async Task UnlockIsNotSynthesizedWhenTheUnitsOwnImportedGoalsAlreadyIncludeOne()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []);

        var body = await ImportGoalsAsync(
            client, [UnlockGoal("unlock", CharacterId, 1), RankGoal("r1", CharacterId, 2, UnitRank.Stone2)]);

        Assert.DoesNotContain(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == GoalType.Unlock);
        Assert.Equal(V1GoalOutcomeStatus.Created, Assert.Single(body.Outcomes, o => o.SourceGoalId == "unlock").Status);
    }

    [Fact]
    public async Task UnlockIsNotSynthesizedWhenTheAccountAlreadyHasOne()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []);
        await CreateNativeGoalAsync(client, CharacterId, GoalType.Unlock, new CreateGoalConfigRequest());

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2)]);

        Assert.DoesNotContain(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == GoalType.Unlock);
    }

    [Fact]
    public async Task AscensionIsSynthesizedAtTheMinimumSatisfyingTargetAndNoLevelGoalIsCreated()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, UnitProgression.CommonNone, UnitRank.Stone1, xpLevel: 1)]);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Gold1)]);

        var expectedAscension = ProgressionRules.MinimumProgressionForRank(UnitRank.Gold1);

        var ascensionOutcome = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == GoalType.Ascension);
        var rankOutcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "r1");

        var ascensionGoal = await GetGoalAsync(client, ascensionOutcome.GoalId!.Value);
        Assert.Equal(expectedAscension, ascensionGoal.Config.Progression!.End);
        Assert.DoesNotContain(body.Outcomes, o => o.GoalType.ToString() == "Level");

        var rankGoal = await GetGoalAsync(client, rankOutcome.GoalId!.Value);
        Assert.Contains(ascensionOutcome.GoalId.Value, rankGoal.DependsOn);
        Assert.Single(rankGoal.DependsOn);
    }

    [Fact]
    public async Task OnePrerequisiteCoversSeveralImportedGoals()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, UnitProgression.CommonNone, UnitRank.Stone1, xpLevel: 60)]);

        // Rank Gold1 needs EpicRedOneStar (cap index 9); Ability target 30 needs the same tier (cap 35).
        var body = await ImportGoalsAsync(
            client, [RankGoal("r1", CharacterId, 1, UnitRank.Gold1), AbilityGoal("ab1", CharacterId, 2, 30, 0)]);

        var ascensionOutcomes = body.Outcomes.Where(o => o.Code == "prerequisite_added" && o.GoalType == GoalType.Ascension).ToList();
        Assert.Single(ascensionOutcomes);
        var ascensionGoalId = ascensionOutcomes[0].GoalId!.Value;

        var rankGoal = await GetGoalAsync(client, Assert.Single(body.Outcomes, o => o.SourceGoalId == "r1").GoalId!.Value);
        var abilityGoal = await GetGoalAsync(client, Assert.Single(body.Outcomes, o => o.SourceGoalId == "ab1").GoalId!.Value);
        Assert.Contains(ascensionGoalId, rankGoal.DependsOn);
        Assert.Contains(ascensionGoalId, abilityGoal.DependsOn);
    }

    [Fact]
    public async Task AnImportedAscensionGoalBelowWhatAnotherGoalNeedsSuppressesSynthesisAndReportsTheShortfall()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, UnitProgression.CommonNone, UnitRank.Stone1, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client,
            [
                AscensionGoal("a1", CharacterId, 1, UnitProgression.UncommonTwoStars), // low target
                RankGoal("r1", CharacterId, 2, UnitRank.Gold1), // needs EpicRedOneStar, higher than a1's target
            ]);

        Assert.DoesNotContain(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == GoalType.Ascension);
        var shortfall = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_target_insufficient");
        Assert.Equal(V1GoalOutcomeStatus.Skipped, shortfall.Status);

        var ascensionOutcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "a1" && o.Status == V1GoalOutcomeStatus.Created);
        var ascensionGoal = await GetGoalAsync(client, ascensionOutcome.GoalId!.Value);
        Assert.Equal(UnitProgression.UncommonTwoStars, ascensionGoal.Config.Progression!.End); // unchanged
    }

    [Fact]
    public async Task UnlockIsNeverSynthesizedForAMachineOfWarNotInTheRoster()
    {
        // Unlock is only ever a valid goal type for a Character (GoalTargetValidationService); a Mow
        // absent from the roster must not get a synthesized Unlock attempt.
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []); // account has player data, but not this Mow

        var body = await ImportGoalsAsync(client, [MowAbilityGoal("mow-ab", MowId, 1, 5, 0)]);

        Assert.DoesNotContain(body.Outcomes, o => o.GoalType == GoalType.Unlock);
        var outcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "mow-ab");
        Assert.Equal(V1GoalOutcomeStatus.Created, outcome.Status);
    }

    [Fact]
    public async Task PrerequisitesAreNotCreatedWhenNotSelected()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2)], automaticPrerequisites: false);

        Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Created, body.Outcomes[0].Status);
    }

    [Fact]
    public async Task ARequirementAlreadyMetNeedsNoPrerequisite()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, UnitProgression.MythicMythicWings, UnitRank.Adamantine1, xpLevel: 60)]);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Adamantine2)]);

        Assert.Single(body.Outcomes);
        Assert.Equal(V1GoalOutcomeStatus.Created, body.Outcomes[0].Status);
    }

    // ----- Ordering -----

    [Fact]
    public async Task ImportPreservesV1sExactInterleavedPriorityOrder()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60), Character("blackTerminator", xpLevel: 60)]);

        // "blackTerminator" first appears at priority 1, then CharacterId at 2, then blackTerminator
        // again at 3 — this exact interleaved sequence must survive the import, not collapse into
        // unit-contiguous blocks (add-inline-goal-reprioritize: priority is flat per-goal now).
        var body = await ImportGoalsAsync(
            client,
            [
                RankGoal("bt-1", "blackTerminator", 1, UnitRank.Iron1),
                RankGoal("ci-1", CharacterId, 2, UnitRank.Iron1),
                AscensionGoal("bt-2", "blackTerminator", 3, UnitProgression.CommonOneStar),
            ],
            automaticPrerequisites: false);

        var importProject = Assert.Single(await GetImportProjectsAsync(client));
        var members = await client.GetProjectGoalsAsync(importProject.ProjectId);
        var byGoalId = members!.Goals.ToDictionary(entry => entry.Goal.GoalId, entry => entry.Goal.GlobalPriority);

        var bt1GoalId = body.Outcomes.Single(o => o.SourceGoalId == "bt-1").GoalId!.Value;
        var ci1GoalId = body.Outcomes.Single(o => o.SourceGoalId == "ci-1").GoalId!.Value;
        var bt2GoalId = body.Outcomes.Single(o => o.SourceGoalId == "bt-2").GoalId!.Value;
        Assert.True(byGoalId[bt1GoalId] < byGoalId[ci1GoalId]);
        Assert.True(byGoalId[ci1GoalId] < byGoalId[bt2GoalId]);
    }

    [Fact]
    public async Task APrerequisitePrecedesTheGoalThatDependsOnIt()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2)]);
        var unlockGoalId = Assert.Single(body.Outcomes, o => o.GoalType == GoalType.Unlock).GoalId!.Value;
        var rankGoalId = Assert.Single(body.Outcomes, o => o.SourceGoalId == "r1").GoalId!.Value;

        var importProject = Assert.Single(await GetImportProjectsAsync(client));
        var members = await client.GetProjectGoalsAsync(importProject.ProjectId);
        var byGoalId = members!.Goals.ToDictionary(entry => entry.Goal.GoalId, entry => entry.Goal.GlobalPriority);

        Assert.True(byGoalId[unlockGoalId] < byGoalId[rankGoalId]);
    }

    // ----- Each import gets its own project -----

    [Fact]
    public async Task ImportedGoalsGoIntoAProjectNamedAfterTheImport()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1)]);

        var project = Assert.Single(await GetImportProjectsAsync(client));
        Assert.Matches(@"^V1 import · \d{1,2} [A-Z][a-z]{2}, \d{2}:\d{2} UTC$", project.Name);
        Assert.Equal((Guid?)project.ProjectId, body.ImportProjectId);
        Assert.Equal(
            body.Outcomes.Where(o => o.GoalId is not null).Select(o => o.GoalId!.Value).Order(),
            project.GoalIds.Order());
    }

    [Fact]
    public async Task AnImportThatCreatesNoGoalsCreatesNoProject()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);
        var goals = new[] { RankGoal("r1", CharacterId, 1, UnitRank.Iron1) };

        await ImportGoalsAsync(client, goals);
        var rerun = await ImportGoalsAsync(client, goals);

        Assert.Single(await GetImportProjectsAsync(client));
        Assert.Null(rerun.ImportProjectId);
    }

    // ----- Imported status follows V1's own dailyRaids choice -----

    [Fact]
    public async Task ImportHonoursEachGoalsPlanningChoice()
    {
        // Status comes from the source goal's own dailyRaids flag, never from project membership
        // (goal-lifecycle-status).
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60), Character(OtherCharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client,
            [
                RankGoal("planned", CharacterId, 1, UnitRank.Iron1, dailyRaids: true),
                RankGoal("excluded", OtherCharacterId, 2, UnitRank.Iron1, dailyRaids: false),
            ],
            automaticPrerequisites: false);

        Assert.Equal(GoalStatus.Active, await StatusOfSourceGoalAsync(client, body, "planned"));
        Assert.Equal(GoalStatus.Paused, await StatusOfSourceGoalAsync(client, body, "excluded"));
    }

    [Fact]
    public async Task AGoalWithNoDailyRaidsFlagImportsActive()
    {
        // A V1 record written before the flag existed must not be pushed into Paused by a defaulted false.
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1, dailyRaids: null)], automaticPrerequisites: false);

        Assert.Equal(GoalStatus.Active, await StatusOfSourceGoalAsync(client, body, "r1"));
    }

    [Fact]
    public async Task ASynthesizedPrerequisiteIsActiveWhenAnyOfItsUnitsGoalsIs()
    {
        // The prerequisite exists to unblock the unit's goals — pausing it while an Active goal depends on
        // it would leave that goal unworkable.
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []); // unit absent from the roster, so Unlock is synthesized

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2, dailyRaids: true)]);

        var unlock = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == GoalType.Unlock);
        Assert.Equal(GoalStatus.Active, (await GetGoalAsync(client, unlock.GoalId!.Value)).Status);
        Assert.Equal(GoalStatus.Active, await StatusOfSourceGoalAsync(client, body, "r1"));
    }

    [Fact]
    public async Task ASynthesizedPrerequisiteIsPausedWhenEveryGoalOfItsUnitIs()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2, dailyRaids: false)]);

        var unlock = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == GoalType.Unlock);
        Assert.Equal(GoalStatus.Paused, (await GetGoalAsync(client, unlock.GoalId!.Value)).Status);
        Assert.Equal(GoalStatus.Paused, await StatusOfSourceGoalAsync(client, body, "r1"));
    }

    [Fact]
    public async Task MergedDuplicateGoalsKeepTheActiveChoice()
    {
        // The survivor stands in for both V1 goals, so merging must not discard the activation the user set
        // on one of them.
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client,
            [
                RankGoal("excluded", CharacterId, 1, UnitRank.Iron1, dailyRaids: false),
                RankGoal("planned", CharacterId, 2, UnitRank.Iron1, dailyRaids: true),
            ],
            automaticPrerequisites: false);

        var created = Assert.Single(body.Outcomes, o => o.Status == V1GoalOutcomeStatus.Created);
        Assert.Equal(GoalStatus.Active, (await GetGoalAsync(client, created.GoalId!.Value)).Status);
    }

    // ----- Idempotency -----

    [Fact]
    public async Task ReimportingTheSameProfileCreatesNoDuplicatesAndReportsSkipped()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);
        var goals = new[] { RankGoal("r1", CharacterId, 1, UnitRank.Iron1) };

        var first = await ImportGoalsAsync(client, goals);
        var second = await ImportGoalsAsync(client, goals);

        Assert.Equal(V1GoalOutcomeStatus.Created, first.Outcomes[0].Status);
        Assert.Equal(V1GoalOutcomeStatus.Skipped, second.Outcomes[0].Status);
        Assert.Equal("goal_already_exists", second.Outcomes[0].Code);
        Assert.Equal(first.Outcomes[0].GoalId, second.Outcomes[0].GoalId);

        var allGoals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/goals", TestContext.Current.CancellationToken);
        Assert.Single(allGoals!.Goals);
    }

    // ----- Helpers -----

    private async Task<(HttpClient Client, string Subject)> CreateProvisionedClientAsync()
    {
        var subject = $"v1-goal-import-{Guid.NewGuid()}";
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, subject);
        await client.GetAsync("/api/v1/account", TestContext.Current.CancellationToken);
        return (client, subject);
    }

    private async Task SeedPlayerDataSnapshotAsync(
        string subject, List<PlayerCharacterRecord>? characters = null, List<PlayerMowRecord>? mows = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var profile = await db.Profiles.IgnoreQueryFilters()
            .FirstAsync(entity => entity.Subject == subject, TestContext.Current.CancellationToken);
        db.PlayerDataSnapshots.Add(new PlayerDataSnapshot
        {
            Id = profile.Id,
            Characters = characters ?? [],
            Mows = mows ?? [],
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static PlayerCharacterRecord Character(
        string id,
        UnitProgression progression = UnitProgression.CommonNone,
        UnitRank rank = UnitRank.Stone1,
        int xpLevel = 1,
        int activeAbility = 0,
        int passiveAbility = 0) => new()
        {
            UnitId = UnitId.From(id),
            ProgressionIndex = progression,
            Rank = rank,
            XpLevel = xpLevel,
            Abilities =
        [
            new PlayerUnitAbilityRecord { AbilityId = "active", Level = activeAbility },
            new PlayerUnitAbilityRecord { AbilityId = "passive", Level = passiveAbility },
        ],
        };

    private static PlayerMowRecord Mow(string id, UnitProgression progression = UnitProgression.CommonNone) => new()
    {
        UnitId = UnitId.From(id),
        ProgressionIndex = progression,
    };

    private async Task<ImportV1ProfileResponse> ImportGoalsAsync(
        HttpClient client, IReadOnlyList<V1Goal> goals, bool automaticPrerequisites = true)
    {
        var username = factory.V1Client.ConfigureProfile(new TacticusV1Profile(
            null, null, null, goals, V1OnslaughtImportData.Missing(), V1CampaignEventProgressImportData.Missing()));
        var response = await client.PostAsJsonAsync(
            "/api/v1/account/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(false, false, false, true, false, false, automaticPrerequisites)),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<GoalDetailResponse> CreateNativeGoalAsync(
        HttpClient client, string entityId, GoalType goalType, CreateGoalConfigRequest config)
    {
        var response = await client.PostGoalAsync(new CreateGoalRequest(GoalEntityType.Character, entityId, goalType, config, null));
        response.EnsureSuccessStatusCode();
        return (await response.ReadCreatedGoalAsync())!;
    }

    private static async Task<GoalStatus> StatusOfSourceGoalAsync(
        HttpClient client, ImportV1ProfileResponse body, string sourceGoalId)
    {
        var outcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == sourceGoalId);
        Assert.Equal(V1GoalOutcomeStatus.Created, outcome.Status);
        return (await GetGoalAsync(client, outcome.GoalId!.Value)).Status;
    }

    private static async Task<GoalDetailResponse> GetGoalAsync(HttpClient client, Guid goalId) =>
        (await client.GetFromJsonAsync<GoalDetailResponse>($"/api/v1/goals/{goalId}", TestContext.Current.CancellationToken))!;

    private static Guid GoalIdOf(ImportV1ProfileResponse body, string sourceGoalId) =>
        Assert.Single(body.Outcomes, o => o.SourceGoalId == sourceGoalId).GoalId!.Value;

    private static async Task<Dictionary<Guid, int?>> ImportPrioritiesAsync(HttpClient client)
    {
        var importProject = Assert.Single(await GetImportProjectsAsync(client));
        var members = await client.GetProjectGoalsAsync(importProject.ProjectId);
        return members!.Goals.ToDictionary(entry => entry.Goal.GoalId, entry => (int?)entry.Goal.GlobalPriority);
    }

    private static async Task<List<ProjectSummaryResponse>> GetImportProjectsAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/projects", TestContext.Current.CancellationToken);
        return response!.Projects.Where(project => !project.IsDefault).ToList();
    }

    private static V1Goal RankGoal(
        string id, string character, int priority, UnitRank target, string? notes = null, int rankAppliedUpgrades = 0,
        bool? dailyRaids = true) =>
        new(id, character, 1, priority, dailyRaids, notes, null, null, null, (int)target + 1, false, rankAppliedUpgrades,
            null, null, null, null, null, null, null);

    private static V1Goal AscensionGoal(
        string id, string character, int priority, UnitProgression target, string? notes = null,
        string? shardFarmType = null, int? campaignsUsage = null, int? mythicCampaignsUsage = null,
        bool? dailyRaids = true)
    {
        var (rarity, stars) = ProgressionWireParts(target);
        return new(id, character, 2, priority, dailyRaids, notes, null, null, null, null, null, null,
            null, null, rarity, stars, null, null, null, shardFarmType, campaignsUsage, mythicCampaignsUsage);
    }

    private static V1Goal UnlockGoal(string id, string character, int priority, int? campaignsUsage = null) =>
        new(id, character, 3, priority, true, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, campaignsUsage, null);

    private static V1Goal AbilityGoal(string id, string entityId, int priority, int firstAbilityLevel, int secondAbilityLevel) =>
        new(id, entityId, 5, priority, true, null, null, null, null, null, null, null,
            null, null, null, null, null, firstAbilityLevel, secondAbilityLevel);

    private static V1Goal MowAbilityGoal(string id, string mowId, int priority, int firstAbilityLevel, int secondAbilityLevel) =>
        new(id, null, 4, priority, true, null, null, null, null, null, null, null,
            null, null, null, null, mowId, firstAbilityLevel, secondAbilityLevel);

    // No V1 PersonalGoalType has this value; every real V1 type (1-7) imports or reports a specific outcome.
    private static V1Goal UnsupportedGoal(string id, int priority, int type = 99) =>
        new(id, "irrelevant", type, priority, false, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null);

    private static V1Goal UpgradeMaterialGoal(string id, int priority, string upgradeMaterialId, int quantity) =>
        new(id, "", 6, priority, true, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null,
            UpgradeMaterialId: upgradeMaterialId, UpgradeMaterialQuantity: quantity);

    private static V1Goal PreFarmGoal(string id, int priority, string upgradeMaterialId, params string[] linkedGoalIds) =>
        new(id, "", 7, priority, true, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null,
            UpgradeMaterialId: upgradeMaterialId, PreFarmGoalIds: linkedGoalIds);

    private static (int Rarity, int Stars) ProgressionWireParts(UnitProgression progression)
    {
        var wire = ProgressionRules.ProgressionOrder[(int)progression];
        var parts = wire.Split(':');
        return (Array.IndexOf(RarityNames, parts[0]), Array.IndexOf(StarNames, parts[1]));
    }
}
