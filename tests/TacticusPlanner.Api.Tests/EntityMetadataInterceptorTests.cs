using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TacticusPlanner.Domain.EventShops;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.Interceptors;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// The interceptor's owned-entity rule, checked on the change tracker alone (no database): a change to an owned
/// JSON column must bump its owner's revision without the writer touching the row.
/// </summary>
public sealed class EntityMetadataInterceptorTests
{
    [Fact]
    public void ReplacingOnlyTheJsonEntriesBumpsTheOwnersRevisionAndUpdatedAt()
    {
        using var db = CreateContext();
        var plan = StoredPlan();
        db.EventShopPlans.Attach(plan);

        plan.Entries = [Entry("THU")];
        RunInterceptor(db);

        Assert.Equal(EntityState.Modified, db.Entry(plan).State);
        Assert.Equal(4, plan.Revision);
        Assert.True(db.Entry(plan).Property(entity => entity.UpdatedAt).IsModified);
    }

    [Fact]
    public void EditingOneEntryInPlaceAlsoBumpsTheRevision()
    {
        using var db = CreateContext();
        var plan = StoredPlan();
        db.EventShopPlans.Attach(plan);

        plan.Entries[0].PurchasedQuantity = 1;
        RunInterceptor(db);

        Assert.Equal(4, plan.Revision);
    }

    [Fact]
    public void AnUntouchedPlanKeepsItsRevision()
    {
        using var db = CreateContext();
        var plan = StoredPlan();
        db.EventShopPlans.Attach(plan);

        RunInterceptor(db);

        Assert.Equal(EntityState.Unchanged, db.Entry(plan).State);
        Assert.Equal(3, plan.Revision);
    }

    private static EventShopPlan StoredPlan() => new()
    {
        Id = EventShopPlanId.From(Guid.NewGuid()),
        ProfileId = ProfileId.From(Guid.NewGuid()),
        EventId = "october-2026",
        CatalogVersion = "v1",
        Revision = 3,
        Entries = [Entry("MON")],
    };

    private static EventShopCartEntry Entry(string day) =>
        new() { Week = 1, Day = day, SlotIndex = 1, RewardType = "shards_astraYarrick", Quantity = 2 };

    private static void RunInterceptor(DbContext db) =>
        new EntityMetadataInterceptor(TimeProvider.System).SavingChanges(new DbContextEventData(null!, null!, db), default);

    private static PlannerDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<PlannerDbContext>().UseNpgsql("Host=unused").Options,
            new PassthroughEncryption(),
            new NoProfile());

    private sealed class PassthroughEncryption : IColumnEncryptionService
    {
        public string? Encrypt(string? plaintext, string column) => plaintext;

        public string? Decrypt(string? envelope, string column) => envelope;
    }

    private sealed class NoProfile : ICurrentProfileProvider
    {
        public ProfileId? ProfileId => null;
    }
}
