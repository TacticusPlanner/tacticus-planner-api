using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Covers `rewrite-v1-goal-import`'s `v1-goal-import` capability: server-side goal creation (no
/// returned specs), the goals-refused-without-player-data rule, per-source-goal outcomes and their
/// skip/not-imported/failed classification, V1 shard-source translation, V1 notes, automatic
/// prerequisite synthesis, ordering, and idempotency. Uses <see cref="FakeTacticusV1Client.ConfigureProfile"/>
/// so each test supplies its own V1 goal list without touching the shared fixed fixtures.
/// </summary>
public sealed class V1GoalImportEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string CharacterId = "ultraInceptorSgt"; // catalog Name "Bellator" (see TacticusTestDoubles.cs)
    private const string OtherCharacterId = "blackTerminator";
    private const string MowId = "astraOrdnanceBattery";

    private static readonly string[] RarityNames = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];
    private static readonly string[] StarNames =
    [
        "None", "OneStar", "TwoStars", "ThreeStars", "FourStars", "FiveStars", "RedOneStar",
        "RedTwoStars", "RedThreeStars", "RedFourStars", "RedFiveStars", "OneBlueStar",
        "TwoBlueStars", "ThreeBlueStars", "MythicWings",
    ];

    // ----- Refusal without player data -----

    [Fact]
    public async Task GoalsAreRefusedWithoutPlayerDataAndNoGoalsAreCreated()
    {
        var (client, _) = await CreateProvisionedClientAsync();

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1)]);

        Assert.Equal("Failed", body.Goals.Status);
        Assert.Equal("player_data_required", body.Goals.Code);
        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal("Failed", outcome.Status);
        Assert.Equal("player_data_required", outcome.Code);

        var goals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/me/goals", TestContext.Current.CancellationToken);
        Assert.Empty(goals!.Goals);
    }

    [Fact]
    public async Task GoalsSyncAndImportSucceedOnTheFirstRequestWhenAValidKeyIsAvailable()
    {
        // A selected personal key gives EnsurePlayerDataSyncedAsync something to sync with before goals
        // run, so — unlike GoalsAreRefusedWithoutPlayerDataAndNoGoalsAreCreated above, where no key is
        // available anywhere — this first request no longer needs a second, manually-seeded run.
        var (client, subject) = await CreateProvisionedClientAsync();
        var username = FakeTacticusV1Client.ConfigureProfile(new TacticusV1Profile(
            FakeTacticusApi.ValidKey, "some-user-id", null, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1)],
            V1OnslaughtImportData.Missing(), V1CampaignEventProgressImportData.Missing()));

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(true, true, false, true, false, false)),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);

        Assert.Equal("Imported", body!.PersonalTacticusApiKey.Status);
        Assert.Equal("Imported", body.TacticusUserId.Status);
        Assert.Equal("Imported", body.Goals.Status);
        // The character starts unowned in the synced snapshot, so the Rank goal also pulls in its
        // Unlock/Level prerequisites automatically — all three still land as "Created", not refused.
        Assert.All(body.Outcomes, outcome => Assert.Equal("Created", outcome.Status));
        Assert.Contains(body.Outcomes, outcome => outcome.SourceGoalId == "r1");
        _ = subject;
    }

    [Fact]
    public async Task OtherSelectedPartsStillProcessWhenGoalsAreRefusedForLackOfAKeyToSyncWith()
    {
        var (client, _) = await CreateProvisionedClientAsync();
        var username = FakeTacticusV1Client.ConfigureProfile(new TacticusV1Profile(
            null, "some-user-id", null, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1)],
            V1OnslaughtImportData.Missing(), V1CampaignEventProgressImportData.Missing()));

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(false, true, false, true, false, false)),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken);

        Assert.Equal("Failed", body!.Goals.Status);
        Assert.Equal("player_data_required", body.Goals.Code);
        Assert.Equal("Imported", body.TacticusUserId.Status);
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
        Assert.Equal("Created", outcome.Status);
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
        Assert.All(body.Outcomes, outcome => Assert.Equal("Created", outcome.Status));
        Assert.Contains(body.Outcomes, o => o.GoalType == "Rank");
        Assert.Contains(body.Outcomes, o => o.GoalType == "Ascension");
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
        Assert.Equal("Skipped", outcome.Status);
        Assert.Equal("target_already_reached", outcome.Code);
    }

    [Fact]
    public async Task AlreadyExistingGoalIsSkippedAndCarriesTheExistingGoalId()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);
        var existing = await CreateNativeGoalAsync(client, CharacterId, "ascension",
            new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest("Common:None", "Common:TwoStars")));

        var body = await ImportGoalsAsync(client, [AscensionGoal("a1", CharacterId, 1, UnitProgression.CommonOneStar)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal("Skipped", outcome.Status);
        Assert.Equal("goal_already_exists", outcome.Code);
        Assert.Equal(existing.GoalId, outcome.GoalId);
    }

    [Fact]
    public async Task DuplicateRankGoalsMergeIntoOneSpanningGoalAndCombineNotes()
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

        var created = Assert.Single(body.Outcomes, o => o.Status == "Created");
        var merged = Assert.Single(body.Outcomes, o => o.Status == "Skipped");
        Assert.Equal("duplicate_goal_merged", merged.Code);
        Assert.Equal(created.GoalId, merged.GoalId);
        Assert.Equal("r-low", created.SourceGoalId); // higher-priority (earlier) source keeps the slot
        Assert.Equal("r-high", merged.SourceGoalId);

        var goal = await GetGoalAsync(client, created.GoalId!.Value);
        Assert.Equal((int)UnitRank.Silver1, goal.Config.Rank!.End); // spans to the higher target
        Assert.Contains("first", goal.Notes);
        Assert.Contains("second", goal.Notes);
    }

    [Fact]
    public async Task DuplicateRankGoalsWithTheSameEndKeepTheFurtherAlongPointFive()
    {
        // Both goals target the same integer rank, but the second carries a "point five" the first
        // doesn't — comparing only the integer End would arbitrarily keep whichever goal came first
        // instead of the actually-further-along endpoint (see MaxBy's tuple key in CollapseDuplicates).
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);
        var target = (int)UnitRank.Bronze1 + 1;

        var body = await ImportGoalsAsync(
            client,
            [
                new V1Goal("r-plain", CharacterId, 1, 1, true, null, null, null, null, target, false, 0,
                    null, null, null, null, null, null, null),
                new V1Goal("r-pointfive", CharacterId, 1, 2, true, null, null, null, null, target, true, 0,
                    null, null, null, null, null, null, null),
            ],
            automaticPrerequisites: false);

        var created = Assert.Single(body.Outcomes, o => o.Status == "Created");
        var goal = await GetGoalAsync(client, created.GoalId!.Value);
        Assert.True(goal.Config.Rank!.EndPointFive);
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

        var created = Assert.Single(body.Outcomes, o => o.Status == "Created");
        var goal = await GetGoalAsync(client, created.GoalId!.Value);
        var kinds = goal.Config.AcquisitionSources!.Select(source => source.Kind).ToHashSet();
        Assert.Contains("Campaign", kinds);
        Assert.Contains("Onslaught", kinds);
    }

    [Fact]
    public async Task AnExistingAscensionGoalThatAlreadyReachesTheNeededTargetReportsNoShortfall()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);
        await CreateNativeGoalAsync(client, CharacterId, "ascension",
            new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest("Common:None", "Mythic:MythicWings")));

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Gold1)]);

        Assert.DoesNotContain(body.Outcomes, o => o.Code == "prerequisite_target_insufficient");
        Assert.DoesNotContain(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == "Ascension");
        var rankOutcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "r1");
        Assert.Equal("Created", rankOutcome.Status);
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

        var created = Assert.Single(body.Outcomes, o => o.Status == "Created");
        var merged = Assert.Single(body.Outcomes, o => o.Status == "Skipped");
        Assert.Equal("ab-low", created.SourceGoalId);
        Assert.Equal("duplicate_goal_merged", merged.Code);
    }

    [Fact]
    public async Task UnsupportedV1GoalTypeIsNotImportedAndPromisesNoFutureSupport()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId)]);

        var body = await ImportGoalsAsync(client, [UnsupportedGoal("u1", 1, type: 7)]);

        var outcome = Assert.Single(body.Outcomes);
        Assert.Equal("Failed", outcome.Status);
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
        Assert.Equal("Failed", outcome.Status);
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
        Assert.Equal("Failed", outcome.Status);
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
        Assert.Equal("Failed", outcome.Status);
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
        Assert.Equal("Created", valid.Status);
        Assert.Equal("Failed", rejected.Status);
        Assert.Equal("target_rejected", rejected.Code);

        var goals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/me/goals", TestContext.Current.CancellationToken);
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
        Assert.Contains(sources, s => s.Kind == "Onslaught");
        Assert.DoesNotContain(sources, s => s.Kind == "Campaign");
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
        Assert.Contains(sources, s => s.Kind == "Onslaught");
        Assert.Contains(sources, s => s.Kind == "Campaign");
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
        Assert.Equal("Created", outcome.Status);
        var goal = await GetGoalAsync(client, outcome.GoalId!.Value);
        Assert.DoesNotContain(goal.Config.AcquisitionSources ?? [], s => s.Kind == "Onslaught");
        Assert.Contains(goal.Config.AcquisitionSources ?? [], s => s.Kind == "Campaign");
    }

    // ----- Automatic prerequisite synthesis -----

    [Fact]
    public async Task UnlockIsCreatedForAUnitNotInTheRosterAndWiredAsADependency()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []); // account has player data, but not this unit

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2)]);

        var unlock = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == "Unlock");
        var rankOutcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "r1");
        Assert.Equal("Created", rankOutcome.Status);
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

        Assert.DoesNotContain(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == "Unlock");
        Assert.Equal("Created", Assert.Single(body.Outcomes, o => o.SourceGoalId == "unlock").Status);
    }

    [Fact]
    public async Task UnlockIsNotSynthesizedWhenTheAccountAlreadyHasOne()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []);
        await CreateNativeGoalAsync(client, CharacterId, "unlock", new CreateGoalConfigRequest());

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2)]);

        Assert.DoesNotContain(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == "Unlock");
    }

    [Fact]
    public async Task AscensionIsSynthesizedAtTheMinimumSatisfyingTargetWithALevelPrerequisiteToo()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, UnitProgression.CommonNone, UnitRank.Stone1, xpLevel: 1)]);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Gold1)]);

        var expectedAscension = ProgressionRules.MinimumProgressionForRank(UnitRank.Gold1);
        var expectedLevel = ProgressionRules.RequiredLevelForRankTarget(UnitRank.Gold1, false, 0);

        var ascensionOutcome = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == "Ascension");
        var levelOutcome = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == "Level");
        var rankOutcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "r1");

        var ascensionGoal = await GetGoalAsync(client, ascensionOutcome.GoalId!.Value);
        Assert.Equal(ProgressionRules.ProgressionOrder[(int)expectedAscension], ascensionGoal.Config.Progression!.End);
        var levelGoal = await GetGoalAsync(client, levelOutcome.GoalId!.Value);
        Assert.Equal(expectedLevel, levelGoal.Config.Level!.End);

        var rankGoal = await GetGoalAsync(client, rankOutcome.GoalId!.Value);
        Assert.Contains(ascensionOutcome.GoalId.Value, rankGoal.DependsOn);
        Assert.Contains(levelOutcome.GoalId.Value, rankGoal.DependsOn);
    }

    [Fact]
    public async Task OnePrerequisiteCoversSeveralImportedGoals()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, UnitProgression.CommonNone, UnitRank.Stone1, xpLevel: 60)]);

        // Rank Gold1 needs EpicRedOneStar (cap index 9); Ability target 30 needs the same tier (cap 35).
        var body = await ImportGoalsAsync(
            client, [RankGoal("r1", CharacterId, 1, UnitRank.Gold1), AbilityGoal("ab1", CharacterId, 2, 30, 0)]);

        var ascensionOutcomes = body.Outcomes.Where(o => o.Code == "prerequisite_added" && o.GoalType == "Ascension").ToList();
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

        Assert.DoesNotContain(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == "Ascension");
        var shortfall = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_target_insufficient");
        Assert.Equal("Skipped", shortfall.Status);

        var ascensionOutcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "a1" && o.Status == "Created");
        var ascensionGoal = await GetGoalAsync(client, ascensionOutcome.GoalId!.Value);
        Assert.Equal("Uncommon:TwoStars", ascensionGoal.Config.Progression!.End); // unchanged
    }

    [Fact]
    public async Task UnlockIsNeverSynthesizedForAMachineOfWarNotInTheRoster()
    {
        // Unlock is only ever a valid goal type for a Character (GoalTargetValidationService); a Mow
        // absent from the roster must not get a synthesized Unlock attempt.
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []); // account has player data, but not this Mow

        var body = await ImportGoalsAsync(client, [MowAbilityGoal("mow-ab", MowId, 1, 5, 0)]);

        Assert.DoesNotContain(body.Outcomes, o => o.GoalType == "Unlock");
        var outcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "mow-ab");
        Assert.Equal("Created", outcome.Status);
    }

    [Fact]
    public async Task LevelPrerequisiteAboveTheCharacterLevelCapIsRejectedRatherThanPersisted()
    {
        // Adamantine2 with 5 applied upgrades needs level 64 (RequiredLevelForRankTarget), which exceeds
        // GoalTargetValidationService's 60-level cap. The synthesized Level goal must be rejected — not
        // persisted with an invalid target — while the Rank goal that needed it still succeeds.
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, UnitProgression.CommonNone, UnitRank.Stone1, xpLevel: 1)]);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Adamantine2, rankAppliedUpgrades: 5)]);

        var levelOutcome = Assert.Single(body.Outcomes, o => o.GoalType == "Level");
        Assert.Equal("Failed", levelOutcome.Status);
        Assert.Equal("prerequisite_rejected", levelOutcome.Code);
        Assert.Null(levelOutcome.GoalId);

        var rankOutcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == "r1");
        Assert.Equal("Created", rankOutcome.Status);

        var goals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/me/goals", TestContext.Current.CancellationToken);
        Assert.DoesNotContain(goals!.Goals, goal => goal.GoalType == "Level");
    }

    [Fact]
    public async Task PrerequisitesAreNotCreatedWhenNotSelected()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2)], automaticPrerequisites: false);

        Assert.Single(body.Outcomes);
        Assert.Equal("Created", body.Outcomes[0].Status);
    }

    [Fact]
    public async Task ARequirementAlreadyMetNeedsNoPrerequisite()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, UnitProgression.MythicMythicWings, UnitRank.Adamantine1, xpLevel: 60)]);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Adamantine2)]);

        Assert.Single(body.Outcomes);
        Assert.Equal("Created", body.Outcomes[0].Status);
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

        var defaultProject = await GetDefaultProjectAsync(client);
        var members = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals", TestContext.Current.CancellationToken);
        var byGoalId = members!.Goals.ToDictionary(entry => entry.Goal.GoalId, entry => entry.Priority);

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
        var unlockGoalId = Assert.Single(body.Outcomes, o => o.GoalType == "Unlock").GoalId!.Value;
        var rankGoalId = Assert.Single(body.Outcomes, o => o.SourceGoalId == "r1").GoalId!.Value;

        var defaultProject = await GetDefaultProjectAsync(client);
        var members = await client.GetFromJsonAsync<ListProjectGoalsResponse>(
            $"/api/v1/me/projects/{defaultProject.ProjectId}/goals", TestContext.Current.CancellationToken);
        var byGoalId = members!.Goals.ToDictionary(entry => entry.Goal.GoalId, entry => entry.Priority);

        Assert.True(byGoalId[unlockGoalId] < byGoalId[rankGoalId]);
    }

    // ----- Imported status follows V1's own dailyRaids choice -----

    [Fact]
    public async Task ImportWhileAnotherProjectIsTheActivePlanHonoursEachGoalsPlanningChoice()
    {
        // The import always files into the default project, so under the old membership-derived rule the
        // whole import landed Paused whenever another project was current. Status now comes from the source
        // goal's own dailyRaids flag, never from the active plan (goal-lifecycle-status).
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60), Character(OtherCharacterId, xpLevel: 60)]);
        await MakeAnotherProjectCurrentAsync(client);

        var body = await ImportGoalsAsync(
            client,
            [
                RankGoal("planned", CharacterId, 1, UnitRank.Iron1, dailyRaids: true),
                RankGoal("excluded", OtherCharacterId, 2, UnitRank.Iron1, dailyRaids: false),
            ],
            automaticPrerequisites: false);

        Assert.Equal("Active", await StatusOfSourceGoalAsync(client, body, "planned"));
        Assert.Equal("Paused", await StatusOfSourceGoalAsync(client, body, "excluded"));
    }

    [Fact]
    public async Task AGoalWithNoDailyRaidsFlagImportsActive()
    {
        // A V1 record written before the flag existed must not be pushed into Paused by a defaulted false.
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60)]);

        var body = await ImportGoalsAsync(
            client, [RankGoal("r1", CharacterId, 1, UnitRank.Iron1, dailyRaids: null)], automaticPrerequisites: false);

        Assert.Equal("Active", await StatusOfSourceGoalAsync(client, body, "r1"));
    }

    [Fact]
    public async Task ASynthesizedPrerequisiteIsActiveWhenAnyOfItsUnitsGoalsIs()
    {
        // The prerequisite exists to unblock the unit's goals — pausing it while an Active goal depends on
        // it would leave that goal unworkable.
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []); // unit absent from the roster, so Unlock is synthesized

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2, dailyRaids: true)]);

        var unlock = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == "Unlock");
        Assert.Equal("Active", (await GetGoalAsync(client, unlock.GoalId!.Value)).Status);
        Assert.Equal("Active", await StatusOfSourceGoalAsync(client, body, "r1"));
    }

    [Fact]
    public async Task ASynthesizedPrerequisiteIsPausedWhenEveryGoalOfItsUnitIs()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, []);

        var body = await ImportGoalsAsync(client, [RankGoal("r1", CharacterId, 1, UnitRank.Stone2, dailyRaids: false)]);

        var unlock = Assert.Single(body.Outcomes, o => o.Code == "prerequisite_added" && o.GoalType == "Unlock");
        Assert.Equal("Paused", (await GetGoalAsync(client, unlock.GoalId!.Value)).Status);
        Assert.Equal("Paused", await StatusOfSourceGoalAsync(client, body, "r1"));
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
                RankGoal("planned", CharacterId, 2, UnitRank.Silver1, dailyRaids: true),
            ],
            automaticPrerequisites: false);

        var created = Assert.Single(body.Outcomes, o => o.Status == "Created");
        Assert.Equal("Active", (await GetGoalAsync(client, created.GoalId!.Value)).Status);
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

        Assert.Equal("Created", first.Outcomes[0].Status);
        Assert.Equal("Skipped", second.Outcomes[0].Status);
        Assert.Equal("goal_already_exists", second.Outcomes[0].Code);
        Assert.Equal(first.Outcomes[0].GoalId, second.Outcomes[0].GoalId);

        var allGoals = await client.GetFromJsonAsync<ListGoalsResponse>("/api/v1/me/goals", TestContext.Current.CancellationToken);
        Assert.Single(allGoals!.Goals);
    }

    // ----- Helpers -----

    private async Task<(HttpClient Client, string Subject)> CreateProvisionedClientAsync()
    {
        var subject = $"v1-goal-import-{Guid.NewGuid()}";
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, subject);
        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);
        return (client, subject);
    }

    private async Task SeedPlayerDataSnapshotAsync(
        string subject, List<PlayerCharacterRecord>? characters = null, List<PlayerMowRecord>? mows = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().Include(entity => entity.Profile)
            .FirstAsync(entity => entity.Subject == subject, TestContext.Current.CancellationToken);
        db.PlayerDataSnapshots.Add(new PlayerDataSnapshot
        {
            Id = account.Profile!.Id,
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

    private static async Task<ImportV1ProfileResponse> ImportGoalsAsync(
        HttpClient client, IReadOnlyList<V1Goal> goals, bool automaticPrerequisites = true)
    {
        var username = FakeTacticusV1Client.ConfigureProfile(new TacticusV1Profile(
            null, null, null, goals, V1OnslaughtImportData.Missing(), V1CampaignEventProgressImportData.Missing()));
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/v1-import",
            new ImportV1ProfileRequest(
                username, FakeTacticusV1Client.ValidPassword,
                new ImportV1Selection(false, false, false, true, false, false, automaticPrerequisites)),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportV1ProfileResponse>(TestContext.Current.CancellationToken))!;
    }

    private static async Task<GoalDetailResponse> CreateNativeGoalAsync(
        HttpClient client, string entityId, string goalType, CreateGoalConfigRequest config)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest("character", entityId, goalType, config, null),
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GoalDetailResponse>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Creates a second project and makes it the caller's active plan, so an import (which always
    /// files into the default project) runs while some other project is current.</summary>
    private static async Task MakeAnotherProjectCurrentAsync(HttpClient client)
    {
        var created = await client.PostAsJsonAsync(
            "/api/v1/me/projects",
            new CreateProjectRequest("Event Prep", null, null),
            TestContext.Current.CancellationToken
        );
        created.EnsureSuccessStatusCode();
        var project = await created.Content.ReadFromJsonAsync<ProjectSummaryResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(project);
        var activated = await client.PostAsync(
            $"/api/v1/me/projects/{project.ProjectId}/activate", null, TestContext.Current.CancellationToken);
        activated.EnsureSuccessStatusCode();
    }

    private static async Task<string> StatusOfSourceGoalAsync(
        HttpClient client, ImportV1ProfileResponse body, string sourceGoalId)
    {
        var outcome = Assert.Single(body.Outcomes, o => o.SourceGoalId == sourceGoalId);
        Assert.Equal("Created", outcome.Status);
        return (await GetGoalAsync(client, outcome.GoalId!.Value)).Status;
    }

    private static async Task<GoalDetailResponse> GetGoalAsync(HttpClient client, Guid goalId) =>
        (await client.GetFromJsonAsync<GoalDetailResponse>($"/api/v1/me/goals/{goalId}", TestContext.Current.CancellationToken))!;

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/me/projects", TestContext.Current.CancellationToken);
        return response!.Projects.Single(project => project.IsDefault);
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

    private static V1Goal UnsupportedGoal(string id, int priority, int type = 6) =>
        new(id, "irrelevant", type, priority, false, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null);

    private static (int Rarity, int Stars) ProgressionWireParts(UnitProgression progression)
    {
        var wire = ProgressionRules.ProgressionOrder[(int)progression];
        var parts = wire.Split(':');
        return (Array.IndexOf(RarityNames, parts[0]), Array.IndexOf(StarNames, parts[1]));
    }
}
