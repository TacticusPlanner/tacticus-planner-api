using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.EventShopPlans;
using TacticusPlanner.Domain.EventShops;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>/event-shop-plans over the pinned catalog's October 2026 event (one week starting Monday 2026-10-26; slot 1
/// offers 25 Yarrick shards on MON/THU/SUN with cap 5). See specs/event-shop-plans.</summary>
public sealed class EventShopPlanEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string EventId = "october-2026";
    private const string PlanUrl = $"/api/v1/event-shop-plans/{EventId}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EventShopCartEntryRequest Yarrick(string day, int quantity = 2, int purchased = 0) =>
        new(1, day, 1, "shards_astraYarrick", quantity, purchased, null);

    private static PutEventShopPlanRequest Put(long expectedRevision, params EventShopCartEntryRequest[] entries) =>
        new(expectedRevision, entries);

    [Fact]
    public async Task MissingPlanReadsAsEmptyAndCreatesNothing()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct));

        Assert.Equivalent(new EventShopPlanResponse(EventId, 0, null, []), plan);
        Assert.Empty((await ReadAsync<EventShopPlanListResponse>(await client.GetAsync("/api/v1/event-shop-plans", Ct))).Plans);
    }

    [Fact]
    public async Task MissingPlanOfAnEventOutsideTheCatalogAlsoReadsAsEmpty()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var plan = await ReadPlanAsync(await client.GetAsync("/api/v1/event-shop-plans/may-2025", Ct));

        Assert.Equal(0, plan.Revision);
        Assert.Empty(plan.Entries);
    }

    [Fact]
    public async Task PutCreatesThePlanAndEntriesFollowTheEventsDayOrder()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var saved = await ReadPlanAsync(await client.PutAsJsonAsync(
            PlanUrl, Put(0, Yarrick("SUN"), Yarrick("MON", purchased: 2), Yarrick("THU", quantity: 1)), Ct));

        Assert.Equal(1, saved.Revision);
        Assert.Equal(GameCatalogRelease.Version, saved.CatalogVersion);
        Assert.Equal(["MON", "THU", "SUN"], saved.Entries.Select(entry => entry.Day));
        Assert.Equivalent(saved, await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct)));
        Assert.Equivalent(new[] { saved }, (await ReadAsync<EventShopPlanListResponse>(await client.GetAsync("/api/v1/event-shop-plans", Ct))).Plans);
    }

    [Fact]
    public async Task QuantityAboveTheCapIsA400AndTheCartIsUnchanged()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PutAsJsonAsync(PlanUrl, Put(0, Yarrick("THU", quantity: 6)), Ct);

        await AssertBadRequestAsync(response, "entries[0].quantity");
        Assert.Equal(0, (await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct))).Revision);
    }

    [Fact]
    public async Task RewardNotOfferedThatDayIsA400()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        await AssertBadRequestAsync(await client.PutAsJsonAsync(PlanUrl, Put(0, Yarrick("TUE")), Ct), "entries[0].rewardType");
    }

    [Fact]
    public async Task UnknownEventIsA404ForWrites()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var response = await client.PutAsJsonAsync("/api/v1/event-shop-plans/may-2025", Put(0), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StaleWriteIsA409CarryingTheCurrentPlan()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var first = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, Put(0, Yarrick("THU")), Ct));
        var second = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, Put(first.Revision, Yarrick("SUN")), Ct));

        var (issueCode, current) = await ReadConflictAsync(
            await client.PutAsJsonAsync(PlanUrl, Put(first.Revision, Yarrick("MON")), Ct));

        Assert.Equal("eventShopPlanStale", issueCode);
        Assert.Equivalent(second, current);
    }

    [Fact]
    public async Task ConcurrentFirstWritersCreateExactlyOnePlan()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);

        var results = await Task.WhenAll(
            client.PutAsJsonAsync(PlanUrl, Put(0, Yarrick("THU")), Ct),
            client.PutAsJsonAsync(PlanUrl, Put(0, Yarrick("SUN")), Ct));

        var winner = await ReadPlanAsync(Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK));
        var (issueCode, current) = await ReadConflictAsync(Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal("eventShopPlanStale", issueCode);
        Assert.Equivalent(winner, current);
    }

    [Fact]
    public async Task SameEntriesInAnotherOrderKeepTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var saved = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, Put(0, Yarrick("THU"), Yarrick("SUN")), Ct));

        var again = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, Put(saved.Revision, Yarrick("SUN"), Yarrick("THU")), Ct));

        Assert.Equivalent(saved, again);
    }

    [Fact]
    public async Task EmptyEntriesClearTheCartAndBumpTheRevision()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var saved = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, Put(0, Yarrick("THU")), Ct));

        var cleared = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, Put(saved.Revision), Ct));

        Assert.Equal(saved.Revision + 1, cleared.Revision);
        Assert.Empty(cleared.Entries);
    }

    [Fact]
    public async Task PlansArePrivateToTheirProfile()
    {
        var owner = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        var other = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        await ReadPlanAsync(await owner.PutAsJsonAsync(PlanUrl, Put(0, Yarrick("THU")), Ct));

        var plan = await ReadPlanAsync(await other.GetAsync(PlanUrl, Ct));

        Assert.Equal(0, plan.Revision);
        Assert.Empty(plan.Entries);
    }

    [Fact]
    public async Task DeleteRemovesThePlanUnderTheRevisionRule()
    {
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{PlanUrl}?expectedRevision=0", Ct)).StatusCode);
        var saved = await ReadPlanAsync(await client.PutAsJsonAsync(PlanUrl, Put(0, Yarrick("THU")), Ct));

        var (issueCode, current) = await ReadConflictAsync(await client.DeleteAsync($"{PlanUrl}?expectedRevision=0", Ct));
        Assert.Equal("eventShopPlanStale", issueCode);
        Assert.Equivalent(saved, current);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{PlanUrl}?expectedRevision={saved.Revision}", Ct)).StatusCode);
        Assert.Equal(0, (await ReadPlanAsync(await client.GetAsync(PlanUrl, Ct))).Revision);
        await AssertBadRequestAsync(await client.DeleteAsync(PlanUrl, Ct), "expectedRevision");
    }

    [Fact]
    public async Task AStoredPlanOutlivesItsCatalogEventButRejectsWrites()
    {
        var subject = $"event-shop-retired-{Guid.NewGuid()}";
        var client = await GoalsTestHelpers.CreateProvisionedClientAsync(factory, subject);
        const string retired = "may-2025";
        const string url = $"/api/v1/event-shop-plans/{retired}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
            var profileId = await db.Profiles.IgnoreQueryFilters()
                .Where(profile => profile.Subject == subject)
                .Select(profile => profile.Id)
                .SingleAsync(Ct);
            db.EventShopPlans.Add(new EventShopPlan
            {
                Id = EventShopPlanId.From(Guid.NewGuid()),
                ProfileId = profileId,
                EventId = retired,
                CatalogVersion = "old-catalog",
                Entries =
                [
                    new() { Week = 1, Day = "SUN", SlotIndex = 0, RewardType = "gold", Quantity = 1 },
                    new() { Week = 1, Day = "MON", SlotIndex = 0, RewardType = "gold", Quantity = 1 },
                ],
            });
            await db.SaveChangesAsync(Ct);
        }

        var plan = await ReadPlanAsync(await client.GetAsync(url, Ct));

        Assert.Equal("old-catalog", plan.CatalogVersion);
        Assert.Equal(["MON", "SUN"], plan.Entries.Select(entry => entry.Day));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(url, Put(plan.Revision), Ct)).StatusCode);
    }

    private static async Task<EventShopPlanResponse> ReadPlanAsync(HttpResponseMessage response) =>
        await ReadAsync<EventShopPlanResponse>(response);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.Content.ReadFromJsonAsync<T>(Ct);
        Assert.NotNull(body);
        return body;
    }

    private static async Task<(string IssueCode, EventShopPlanResponse Plan)> ReadConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        return ((await response.ReadIssueCodeAsync())!, await response.ReadCurrentAsync<EventShopPlanResponse>());
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
