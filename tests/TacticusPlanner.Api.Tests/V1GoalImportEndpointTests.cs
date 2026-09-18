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
    public async Task OtherSelectedPartsStillProcessWhenGoalsAreRefused()
    {
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

        Assert.Equal("Failed", body!.Goals.Status);
        Assert.Equal("player_data_required", body.Goals.Code);
        Assert.Equal("Imported", body.PersonalTacticusApiKey.Status);
        Assert.Equal("Imported", body.TacticusUserId.Status);
        _ = subject;
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
    public async Task UnitBlocksAppearInTheOrderTheirUnitsFirstAppearInV1Priority()
    {
        var (client, subject) = await CreateProvisionedClientAsync();
        await SeedPlayerDataSnapshotAsync(subject, [Character(CharacterId, xpLevel: 60), Character("blackTerminator", xpLevel: 60)]);

        // "blackTerminator" first appears at priority 1; CharacterId's goals interleave afterward.
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

        var btGoalIds = body.Outcomes.Where(o => o.SourceGoalId is "bt-1" or "bt-2").Select(o => o.GoalId!.Value);
        var ciGoalId = body.Outcomes.Single(o => o.SourceGoalId == "ci-1").GoalId!.Value;
        Assert.All(btGoalIds, id => Assert.True(byGoalId[id] < byGoalId[ciGoalId]));
    }

    [Fact]
    public async Task APrerequisitePrecedesTheGoalsThatDependOnItWithinItsUnitBlock()
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

    private static async Task<GoalDetailResponse> GetGoalAsync(HttpClient client, Guid goalId) =>
        (await client.GetFromJsonAsync<GoalDetailResponse>($"/api/v1/me/goals/{goalId}", TestContext.Current.CancellationToken))!;

    private static async Task<ProjectSummaryResponse> GetDefaultProjectAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<ListProjectsResponse>(
            "/api/v1/me/projects", TestContext.Current.CancellationToken);
        return response!.Projects.Single(project => project.IsDefault);
    }

    private static V1Goal RankGoal(
        string id, string character, int priority, UnitRank target, string? notes = null, int rankAppliedUpgrades = 0) =>
        new(id, character, 1, priority, true, notes, null, null, null, (int)target + 1, false, rankAppliedUpgrades,
            null, null, null, null, null, null, null);

    private static V1Goal AscensionGoal(
        string id, string character, int priority, UnitProgression target, string? notes = null,
        string? shardFarmType = null, int? campaignsUsage = null, int? mythicCampaignsUsage = null)
    {
        var (rarity, stars) = ProgressionWireParts(target);
        return new(id, character, 2, priority, true, notes, null, null, null, null, null, null,
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
