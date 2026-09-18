using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Covers `fix-goal-ability-cap-effective-progression`'s `goal-target-model` requirements: an Ability
/// target's progression-derived cap is evaluated against the higher of the unit's live progression and
/// the highest Ascension target, within the same combined request, among the specs the Ability goal
/// declares a dependency on — never from mere presence in the request.
/// </summary>
public sealed class GoalAbilityCapEffectiveProgressionTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string CharacterId = "blackTerminator";

    [Fact]
    public async Task AbilityAboveLiveCapIsAcceptedWithADependedUponAscension()
    {
        var (client, _) = await ProvisionWithLiveProgressionAsync(UnitProgression.CommonNone);

        var response = await PostCombinedAsync(client, AscensionThenAbility(
            ascensionEnd: "Uncommon:TwoStars", // rarity Uncommon, cap 17
            abilityActiveEnd: 12, // above the live Common cap (8), at or below the raised cap (17)
            abilityDependsOnAscension: true));

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreateCombinedGoalsResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(created);
        Assert.Equal(2, created.Goals.Count);
    }

    [Fact]
    public async Task AbilityAboveLiveCapIsRejectedWithoutAnAscension()
    {
        var (client, _) = await ProvisionWithLiveProgressionAsync(UnitProgression.CommonNone);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                CharacterId,
                "ability",
                new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 12, 0, 0)),
                null),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AbilityAboveEvenTheAscendedCapIsRejected()
    {
        var (client, _) = await ProvisionWithLiveProgressionAsync(UnitProgression.CommonNone);

        var response = await PostCombinedAsync(client, AscensionThenAbility(
            ascensionEnd: "Uncommon:TwoStars", // raises the cap to 17
            abilityActiveEnd: 20, // still above the raised cap
            abilityDependsOnAscension: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UndeclaredAscensionDoesNotRaiseTheCap()
    {
        var (client, _) = await ProvisionWithLiveProgressionAsync(UnitProgression.CommonNone);

        // A qualifying Ascension spec is present, but the Ability spec declares no dependency on it.
        var response = await PostCombinedAsync(client, AscensionThenAbility(
            ascensionEnd: "Uncommon:TwoStars",
            abilityActiveEnd: 12,
            abilityDependsOnAscension: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DependencyOnANonAscensionSpecDoesNotRaiseTheCap()
    {
        var (client, _) = await ProvisionWithLiveProgressionAsync(UnitProgression.CommonNone);

        var request = new CreateCombinedGoalsRequest(
            "character",
            CharacterId,
            null,
            [
                new CombinedGoalSpec("level", new CreateGoalConfigRequest(Level: new LevelTargetRequest(0, 10)), []),
                new CombinedGoalSpec(
                    "ability",
                    new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 12, 0, 0)),
                    [0]),
            ]);

        var response = await PostCombinedAsync(client, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NoPlayerDataDerivesTheEffectiveProgressionSolelyFromTheDependedUponAscension()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        // No PlayerCharacterRecord recorded at all for this unit: under the old top-of-ladder default this
        // would have been accepted regardless of target; it must now be rejected because the effective
        // progression comes solely from the depended-upon Ascension target (cap 17), not Mythic (cap 60).
        var response = await PostCombinedAsync(client, AscensionThenAbility(
            ascensionEnd: "Uncommon:TwoStars",
            abilityActiveEnd: 25,
            abilityDependsOnAscension: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SingleGoalCreationIsUnaffected()
    {
        // No player data at all -> falls back to the live progression default (top of the ladder), unchanged.
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/me/goals",
            new CreateGoalRequest(
                "character",
                CharacterId,
                "ability",
                new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, 12, 0, 0)),
                null),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static CreateCombinedGoalsRequest AscensionThenAbility(
        string ascensionEnd, int abilityActiveEnd, bool abilityDependsOnAscension) => new(
        "character",
        CharacterId,
        null,
        [
            new CombinedGoalSpec(
                "ascension",
                new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest("Common:None", ascensionEnd)),
                []),
            new CombinedGoalSpec(
                "ability",
                new CreateGoalConfigRequest(Ability: new AbilityTargetRequest(0, abilityActiveEnd, 0, 0)),
                abilityDependsOnAscension ? [0] : []),
        ]);

    private static Task<HttpResponseMessage> PostCombinedAsync(HttpClient client, CreateCombinedGoalsRequest request) =>
        client.PostAsJsonAsync("/api/v1/me/goals/combined", request, TestContext.Current.CancellationToken);

    /// <summary>Provisions a client and seeds a minimal <see cref="PlayerDataSnapshot"/> recording
    /// <see cref="CharacterId"/> at the given live progression — directly through the DI container rather
    /// than a full Tacticus sync, since only <see cref="PlayerBaseUnitRecord.ProgressionIndex"/> matters
    /// here.</summary>
    private async Task<(HttpClient Client, ProfileId ProfileId)> ProvisionWithLiveProgressionAsync(UnitProgression progression)
    {
        var subject = $"ability-cap-{Guid.NewGuid()}";
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters()
            .Include(entity => entity.Profile)
            .FirstAsync(entity => entity.Subject == subject, TestContext.Current.CancellationToken);
        var profileId = account.Profile!.Id;

        db.PlayerDataSnapshots.Add(new PlayerDataSnapshot
        {
            Id = profileId,
            Characters = [new PlayerCharacterRecord
            {
                UnitId = UnitId.From(CharacterId),
                ProgressionIndex = progression,
            }],
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (client, profileId);
    }
}
