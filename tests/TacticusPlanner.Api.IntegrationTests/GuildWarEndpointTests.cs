using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.GuildWars;
using TacticusPlanner.Domain.GuildWars;
using TacticusPlanner.GameDomain;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary><c>guild-war-offense</c>: the current war, starting a new one, past wars, attack create, update and
/// delete with the war rules, isolation, and purge.</summary>
public sealed class GuildWarEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string Url = "/api/v1/guild-wars";
    private const string CurrentUrl = "/api/v1/guild-wars/current";
    private const string AttacksUrl = "/api/v1/guild-wars/current/attacks";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, string Subject)> ClientAsync()
    {
        var subject = $"guild-wars-{Guid.NewGuid()}";
        return (await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject), subject);
    }

    private static GuildWarLineupUnitDto Unit(string id, UnitRank? rank = UnitRank.Gold1) =>
        new(id, UnitProgression.EpicRedOneStar, rank, 40, [30, 30]);

    private static CreateGuildWarAttackRequest Attack(
        string[] characters,
        string? mow = null,
        string zoneId = "Armoury",
        int slot = 2,
        GuildWarAttackResult? result = null,
        int? unitsLost = null,
        GuildWarEnemyDto? enemy = null) =>
        new(zoneId, slot, [.. characters.Select(id => Unit(id))], mow is null ? null : Unit(mow, rank: null), result,
            unitsLost, enemy, null);

    private static UpdateGuildWarAttackRequest Update(
        GuildWarAttackResponse attack, string[]? characters = null, string? zoneId = null, int? slot = null) =>
        new(zoneId ?? attack.ZoneId, slot ?? attack.Slot, characters is null ? attack.Characters : [.. characters.Select(id => Unit(id))],
            attack.Mow, attack.Result, attack.UnitsLost, attack.Enemy, attack.Revision);

    private static async Task<GuildWarResponse> OkWarAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GuildWarResponse>(Ct))!;
    }

    private static async Task<GuildWarResponse> SetCurrentAsync(
        HttpClient client, string seasonId = "guildWarSeasonA", int level = 5, long revision = 0) =>
        await OkWarAsync(await client.PutAsJsonAsync(CurrentUrl, new SetCurrentGuildWarRequest(seasonId, level, revision), Ct));

    private static async Task<GuildWarResponse> StartAsync(HttpClient client, string seasonId = "guildWarSeasonA", int level = 5) =>
        await OkWarAsync(await client.PostAsJsonAsync(Url, new StartGuildWarRequest(seasonId, level), Ct));

    private static async Task<GuildWarResponse> LogAsync(HttpClient client, CreateGuildWarAttackRequest request) =>
        await OkWarAsync(await client.PostAsJsonAsync(AttacksUrl, request, Ct));

    private static async Task<GuildWarResponse?> CurrentAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CurrentGuildWarResponse>(CurrentUrl, Ct))!.War;

    private static string AttackUrl(GuildWarResponse war, GuildWarAttackResponse attack) =>
        $"{Url}/{war.Id}/attacks/{attack.Id}";

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string issueCode)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.ReadProblemAsync();
        Assert.Equal(issueCode, problem["issueCode"]!.GetValue<string>());
    }

    [Fact]
    public async Task AProfileWithNoWarReadsNull()
    {
        var (client, _) = await ClientAsync();

        Assert.Null(await CurrentAsync(client));
    }

    [Fact]
    public async Task PutCreatesTheCurrentWarThenUpdatesItByRevision()
    {
        var (client, _) = await ClientAsync();

        var created = await SetCurrentAsync(client, "guildWarSeasonA", 3, revision: 42);
        Assert.Equal(("guildWarSeasonA", 3, (DateTimeOffset?)null, 0, 0), (created.SeasonId, created.BattlefieldLevel, created.EndedAt, created.TokensUsed, created.Attacks.Count));

        var updated = await SetCurrentAsync(client, "guildWarSeasonB", 5, created.Revision);
        Assert.Equal((created.Id, "guildWarSeasonB", 5), (updated.Id, updated.SeasonId, updated.BattlefieldLevel));
        Assert.True(updated.Revision > created.Revision);
        AssertSame(updated, await CurrentAsync(client));

        var stale = await client.PutAsJsonAsync(CurrentUrl, new SetCurrentGuildWarRequest("guildWarSeasonC", 1, created.Revision), Ct);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, GuildWarsService.RevisionStale);
        Assert.Equal("guildWarSeasonB", (await CurrentAsync(client))!.SeasonId);
    }

    [Theory]
    [InlineData("guildWarSeasonGone", 1, "guildWarUnknownSeason")]
    [InlineData("guildWarSeasonA", 6, "validation")]
    public async Task SettingsMustNameAServedSeasonAndLevel(string seasonId, int level, string issueCode)
    {
        var (client, _) = await ClientAsync();

        await AssertProblemAsync(
            await client.PutAsJsonAsync(CurrentUrl, new SetCurrentGuildWarRequest(seasonId, level, 0), Ct),
            HttpStatusCode.BadRequest, issueCode);
        await AssertProblemAsync(
            await client.PostAsJsonAsync(Url, new StartGuildWarRequest(seasonId, level), Ct),
            HttpStatusCode.BadRequest, issueCode);
        Assert.Null(await CurrentAsync(client));
    }

    [Fact]
    public async Task StartingANewWarKeepsTheOldOne()
    {
        var (client, _) = await ClientAsync();
        var old = await SetCurrentAsync(client);
        await LogAsync(client, Attack(["ultraCalgar"]));
        await LogAsync(client, Attack(["ultraTitus"], slot: 3));
        await LogAsync(client, Attack(["ultraApothecary"], slot: 4));

        var started = await StartAsync(client, "guildWarSeasonB", 2);

        Assert.NotEqual(old.Id, started.Id);
        Assert.Equal(("guildWarSeasonB", 2, (DateTimeOffset?)null, 0), (started.SeasonId, started.BattlefieldLevel, started.EndedAt, started.TokensUsed));
        AssertSame(started, await CurrentAsync(client));
        var past = (await client.GetFromJsonAsync<PastGuildWarsResponse>(Url, Ct))!;
        var ended = Assert.Single(past.Items);
        Assert.Equal((old.Id, 3), (ended.Id, ended.Attacks.Count));
        Assert.NotNull(ended.EndedAt);
        Assert.Null(past.NextAfter);
    }

    [Fact]
    public async Task StartingTheFirstWarNeedsNoCurrentOne()
    {
        var (client, _) = await ClientAsync();

        var started = await StartAsync(client);

        AssertSame(started, await CurrentAsync(client));
        Assert.Empty((await client.GetFromJsonAsync<PastGuildWarsResponse>(Url, Ct))!.Items);
    }

    [Fact]
    public async Task PastWarsAreListedNewestFirstAcrossPages()
    {
        var (client, _) = await ClientAsync();
        var first = await StartAsync(client);
        var second = await StartAsync(client);
        var third = await StartAsync(client);
        await StartAsync(client);

        var page = (await client.GetFromJsonAsync<PastGuildWarsResponse>($"{Url}?limit=2", Ct))!;
        Assert.Equal([third.Id, second.Id], page.Items.Select(war => war.Id));
        Assert.NotNull(page.NextAfter);

        var next = (await client.GetFromJsonAsync<PastGuildWarsResponse>(
            $"{Url}?limit=2&after={Uri.EscapeDataString(page.NextAfter)}", Ct))!;
        Assert.Equal([first.Id], next.Items.Select(war => war.Id));
        Assert.Null(next.NextAfter);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=51")]
    [InlineData("after=not-a-cursor")]
    public async Task ABadPageRequestIsRefused(string query)
    {
        var (client, _) = await ClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Url}?{query}", Ct)).StatusCode);
    }

    [Fact]
    public async Task LoggingAnAttackDerivesTheCapTokensAndDeployedUnits()
    {
        var (client, _) = await ClientAsync();
        await SetCurrentAsync(client, "guildWarSeasonA", 5);

        var enemy = new GuildWarEnemyDto(
            [new GuildWarEnemyUnitDto("tauAunShi", GuildWarRarity.Epic, UnitRank.Gold2, GuildWarEnemyUnitState.Killed)],
            null, ["EnvArmourSupplies_Regional"], null);
        await LogAsync(client, Attack(["ultraCalgar", "ultraTitus"], "ultraDreadnought", result: GuildWarAttackResult.Won, unitsLost: 1, enemy: enemy));
        var war = await LogAsync(client, Attack(["bloodDante", "bloodMephiston", "ultraApothecary"], zoneId: "Armoury", slot: 0));

        Assert.Equal(2, war.TokensUsed);
        Assert.Equal(["ultraCalgar", "ultraTitus", "ultraDreadnought", "bloodDante", "bloodMephiston", "ultraApothecary"], war.DeployedUnitIds);
        Assert.Equal([GuildWarRarity.Epic, GuildWarRarity.Legendary], war.Attacks.Select(attack => attack.RarityCap));
        var logged = war.Attacks[0];
        Assert.Equal((GuildWarAttackResult.Won, (int?)1), (logged.Result, logged.UnitsLost));
        AssertSame(enemy, logged.Enemy);
        Assert.Null(logged.Mow!.Rank);
        AssertSame(war, await CurrentAsync(client));
    }

    [Fact]
    public async Task AnAttackNeedsACurrentWar()
    {
        var (client, _) = await ClientAsync();

        await AssertProblemAsync(
            await client.PostAsJsonAsync(AttacksUrl, Attack(["ultraCalgar"]), Ct), HttpStatusCode.Conflict, GuildWarsService.NotStarted);
    }

    [Fact]
    public async Task AUnitAttacksOncePerWar()
    {
        var (client, _) = await ClientAsync();
        await SetCurrentAsync(client);
        var before = await LogAsync(client, Attack(["ultraCalgar"]));

        await AssertProblemAsync(
            await client.PostAsJsonAsync(AttacksUrl, Attack(["ultraTitus", "ultraCalgar"]), Ct),
            HttpStatusCode.BadRequest, "guildWarUnitDeployed");
        AssertSame(before, await CurrentAsync(client));
    }

    [Theory]
    [InlineData("NoSuchZone", "ultraCalgar", "guildWarUnknownZone")]
    [InlineData("Armoury", "noSuchCharacter", "guildWarUnknownUnit")]
    public async Task AnUnknownTargetOrUnitIsRefused(string zoneId, string characterId, string issueCode)
    {
        var (client, _) = await ClientAsync();
        await SetCurrentAsync(client);

        await AssertProblemAsync(
            await client.PostAsJsonAsync(AttacksUrl, Attack([characterId], zoneId: zoneId), Ct), HttpStatusCode.BadRequest, issueCode);
    }

    [Fact]
    public async Task TokensRunOut()
    {
        var (client, subject) = await ClientAsync();
        var war = await SetCurrentAsync(client);
        await SeedAttacksAsync(subject, GuildWarId.From(war.Id), 10);

        await AssertProblemAsync(
            await client.PostAsJsonAsync(AttacksUrl, Attack(["ultraCalgar"]), Ct), HttpStatusCode.BadRequest, "guildWarTokensSpent");
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(1, null, 2)]
    [InlineData(1, GuildWarAttackResult.Lost, 7)]
    [InlineData(6, null, null)]
    public async Task AnInvalidShapeIsAValidationError(int characters, GuildWarAttackResult? result, int? unitsLost)
    {
        var (client, _) = await ClientAsync();
        await SetCurrentAsync(client);
        string[] ids = ["ultraCalgar", "ultraTitus", "ultraApothecary", "ultraTigurius", "ultraEliminatorSgt", "ultraInceptorSgt"];

        await AssertProblemAsync(
            await client.PostAsJsonAsync(AttacksUrl, Attack(ids[..characters], result: result, unitsLost: unitsLost), Ct),
            HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task UpdatingAnAttackKeepsItsCapUnlessTheTargetMoves()
    {
        var (client, _) = await ClientAsync();
        var war = await SetCurrentAsync(client, "guildWarSeasonA", 5);
        war = await LogAsync(client, Attack(["ultraCalgar"]));
        var attack = war.Attacks[0];
        // Dropping the level does not change a logged cap, nor does an edit that keeps the target.
        war = await SetCurrentAsync(client, "guildWarSeasonA", 0, war.Revision);
        war = await OkWarAsync(await client.PutAsJsonAsync(AttackUrl(war, attack), Update(attack, ["ultraCalgar", "ultraTitus"]), Ct));
        var kept = Assert.Single(war.Attacks);
        Assert.Equal((attack.Id, GuildWarRarity.Epic, attack.AttackedAt), (kept.Id, kept.RarityCap, kept.AttackedAt));
        Assert.Equal(["ultraCalgar", "ultraTitus"], war.DeployedUnitIds);
        Assert.True(kept.Revision > attack.Revision);

        var moved = await OkWarAsync(await client.PutAsJsonAsync(AttackUrl(war, kept), Update(kept, slot: 0), Ct));
        var expected = ExpectedCap("guildWarSeasonA", 0, "Armoury", 0);
        Assert.Equal(expected, Assert.Single(moved.Attacks).RarityCap);
    }

    [Fact]
    public async Task AStaleAttackEditIsRefused()
    {
        var (client, _) = await ClientAsync();
        await SetCurrentAsync(client);
        var war = await LogAsync(client, Attack(["ultraCalgar"]));
        var attack = war.Attacks[0];
        war = await OkWarAsync(await client.PutAsJsonAsync(AttackUrl(war, attack), Update(attack, ["ultraTitus"]), Ct));

        var stale = await client.PutAsJsonAsync(AttackUrl(war, attack), Update(attack, ["bloodDante"]), Ct);

        await AssertProblemAsync(stale, HttpStatusCode.Conflict, GuildWarsService.AttackRevisionStale);
        AssertSame(war, await CurrentAsync(client));
    }

    [Fact]
    public async Task AnEditMayNotTakeAUnitOfAnotherAttack()
    {
        var (client, _) = await ClientAsync();
        await SetCurrentAsync(client);
        await LogAsync(client, Attack(["ultraCalgar"]));
        var war = await LogAsync(client, Attack(["ultraTitus"]));

        await AssertProblemAsync(
            await client.PutAsJsonAsync(AttackUrl(war, war.Attacks[1]), Update(war.Attacks[1], ["ultraCalgar"]), Ct),
            HttpStatusCode.BadRequest, "guildWarUnitDeployed");
    }

    [Fact]
    public async Task UndoFreesTheTokenAndUnits()
    {
        var (client, _) = await ClientAsync();
        await SetCurrentAsync(client);
        await LogAsync(client, Attack(["ultraTitus"]));
        var war = await LogAsync(client, Attack(["ultraCalgar"]));

        var undone = await OkWarAsync(await client.DeleteAsync(AttackUrl(war, war.Attacks[1]), Ct));

        Assert.Equal(1, undone.TokensUsed);
        Assert.Equal(["ultraTitus"], undone.DeployedUnitIds);
        await LogAsync(client, Attack(["ultraCalgar"]));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(AttackUrl(war, war.Attacks[1]), Ct)).StatusCode);
    }

    [Fact]
    public async Task AnEndedWarsAttacksMayBeEditedAndUndone()
    {
        var (client, _) = await ClientAsync();
        await SetCurrentAsync(client);
        var old = await LogAsync(client, Attack(["ultraCalgar"]));
        await StartAsync(client);

        var edited = await OkWarAsync(await client.PutAsJsonAsync(AttackUrl(old, old.Attacks[0]), Update(old.Attacks[0], ["ultraTitus"]), Ct));
        Assert.Equal(old.Id, edited.Id);
        Assert.Equal(["ultraTitus"], edited.DeployedUnitIds);
        Assert.NotNull(edited.EndedAt);

        Assert.Empty((await OkWarAsync(await client.DeleteAsync(AttackUrl(old, old.Attacks[0]), Ct))).Attacks);
    }

    [Fact]
    public async Task AnotherProfilesWarIsNotFound()
    {
        var (owner, _) = await ClientAsync();
        await SetCurrentAsync(owner);
        var war = await LogAsync(owner, Attack(["ultraCalgar"]));
        var (other, _) = await ClientAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync(AttackUrl(war, war.Attacks[0]), Update(war.Attacks[0], ["ultraTitus"]), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync(AttackUrl(war, war.Attacks[0]), Ct)).StatusCode);
        Assert.Null(await CurrentAsync(other));
        AssertSame(war, await CurrentAsync(owner));
    }

    [Fact]
    public async Task PurgeDeletesTheProfilesWarsOnly()
    {
        var (client, subject) = await ClientAsync();
        await SetCurrentAsync(client);
        await LogAsync(client, Attack(["ultraCalgar"]));
        await StartAsync(client);
        var (other, _) = await ClientAsync();
        await SetCurrentAsync(other);
        var kept = await LogAsync(other, Attack(["ultraCalgar"]));
        var profileId = await ProfileIdAsync(subject);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/account", Ct)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        Assert.False(await db.GuildWars.IgnoreQueryFilters().AnyAsync(war => war.ProfileId == profileId, Ct));
        Assert.False(await db.GuildWarAttacks.IgnoreQueryFilters().AnyAsync(attack => attack.ProfileId == profileId, Ct));
        AssertSame(kept, await CurrentAsync(other));
    }

    private async Task<Domain.Profiles.ProfileId> ProfileIdAsync(string subject)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        return await db.Profiles.IgnoreQueryFilters()
            .Where(profile => profile.Subject == subject).Select(profile => profile.Id).SingleAsync(Ct);
    }

    /// <summary>Writes attacks with empty lineups straight into the war, bypassing the rules.</summary>
    private async Task SeedAttacksAsync(string subject, GuildWarId warId, int count)
    {
        var profileId = await ProfileIdAsync(subject);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        db.GuildWarAttacks.AddRange(Enumerable.Range(0, count).Select(index => new GuildWarAttack
        {
            Id = GuildWarAttackId.From(Guid.CreateVersion7()),
            WarId = warId,
            ProfileId = profileId,
            AttackedAt = DateTimeOffset.UtcNow.AddMinutes(-index),
            ZoneId = "Armoury",
            Slot = 2,
            RarityCap = GuildWarRarity.Epic,
        }));
        await db.SaveChangesAsync(Ct);
    }

    private GuildWarRarity? ExpectedCap(string seasonId, int level, string zoneId, int slot)
    {
        using var scope = factory.Services.CreateScope();
        var facts = scope.ServiceProvider.GetRequiredService<GuildWarsService>().Facts;
        var war = GuildWar.Start(Domain.Profiles.ProfileId.From(Guid.NewGuid()), seasonId, level, DateTimeOffset.UtcNow);
        return GuildWarRules.RarityCap(facts, war, zoneId, slot);
    }

    /// <summary>Compares responses by their wire JSON (records holding lists compare those by reference).</summary>
    private static void AssertSame<T>(T expected, T actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected, TestJson.Options), JsonSerializer.Serialize(actual, TestJson.Options));
}
