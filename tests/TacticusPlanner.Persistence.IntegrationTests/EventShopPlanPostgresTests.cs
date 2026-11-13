using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticusPlanner.Domain.EventShops;
using TacticusPlanner.Domain.Profiles;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>The <c>event_shop_plans</c> table: one row per (profile, event) with the cart as jsonb, deleted with
/// its profile. See specs/event-shop-plans.</summary>
public sealed class EventShopPlanPostgresTests
{
    private static EventShopPlan Plan(ProfileId profileId, string eventId = "october-2026") => new()
    {
        Id = EventShopPlanId.From(Guid.CreateVersion7()),
        ProfileId = profileId,
        EventId = eventId,
        CatalogVersion = "test",
        Entries =
        [
            new() { Week = 1, Day = "THU", SlotIndex = 1, RewardType = "shards_astraYarrick", Quantity = 2, PurchasedQuantity = 1 },
            new() { Week = 1, Day = "MON", SlotIndex = 4, RewardType = "draft_abilityTokensLegendary", Quantity = 1, DraftAlliance = "xenos" },
        ],
    };

    [Fact]
    public async Task AnEventShopPlanRoundTripsAsOneRow()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var plan = Plan(database.ProfileId);
        EventShopValidation.AssertInvariants(plan);
        await using (var db = database.CreateContext())
        {
            db.EventShopPlans.Add(plan);
            await db.SaveChangesAsync(ct);
        }

        await using var verify = database.CreateContext();
        var stored = await verify.EventShopPlans.AsNoTracking().SingleAsync(ct);

        Assert.Equal("october-2026", stored.EventId);
        Assert.Equivalent(plan.Entries, stored.Entries, strict: true);
        Assert.Null(stored.Entries[0].DraftAlliance);
        Assert.Equal(1, stored.Revision);
    }

    [Fact]
    public async Task OnlyOnePlanPerProfileAndEvent()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await using (var db = database.CreateContext())
        {
            db.EventShopPlans.Add(Plan(database.ProfileId));
            await db.SaveChangesAsync(ct);
        }

        await using var second = database.CreateContext();
        second.EventShopPlans.Add(Plan(database.ProfileId));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync(ct));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task PlansAreScopedToTheirProfileAndDeletedWithIt()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var other = ProfileId.From(Guid.CreateVersion7());
        await database.SeedProfileAsync(other);
        await using (var db = database.CreateContext())
        {
            db.EventShopPlans.Add(Plan(database.ProfileId));
            db.EventShopPlans.Add(Plan(database.ProfileId, "june-2026"));
            await db.SaveChangesAsync(ct);
        }

        await using (var otherDb = database.CreateContext(other))
        {
            Assert.Empty(await otherDb.EventShopPlans.ToListAsync(ct));
        }

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var delete = new NpgsqlCommand("DELETE FROM profiles WHERE id = @id", connection);
            delete.Parameters.AddWithValue("id", database.ProfileId.Value);
            await delete.ExecuteNonQueryAsync(ct);
        }

        await using var verify = database.CreateContext();
        Assert.Empty(await verify.EventShopPlans.IgnoreQueryFilters().ToListAsync(ct));
    }
}
