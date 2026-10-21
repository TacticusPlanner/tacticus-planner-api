using Microsoft.EntityFrameworkCore;
using Npgsql;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.GameDomain;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>The single <c>InitialCreate</c> schema: the one raw constraint the EF model cannot describe, and the
/// aggregates stored as one row with jsonb children.</summary>
public sealed class SchemaPostgresTests
{
    /// <summary>Guards the raw statement appended to <c>InitialCreate</c>: a future migration squash that drops it
    /// fails here.</summary>
    [Fact]
    public async Task GlobalPriorityUniquenessIsADeferredConstraint()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.contype, c.condeferrable, c.condeferred, pg_get_constraintdef(c.oid)
            FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid
            WHERE t.relname = 'goals' AND c.conname = 'ix_goals_profile_global_priority';
            """;
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal('u', reader.GetChar(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.Equal("UNIQUE (profile_id, global_priority) DEFERRABLE INITIALLY DEFERRED", reader.GetString(3));
    }

    [Fact]
    public async Task OnlyTheInitialMigrationIsApplied()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        await using var db = database.CreateContext();

        var applied = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.Single(applied, migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ALegendaryEventPlanRoundTripsAsOneRow()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var recordedAt = new DateTimeOffset(2026, 10, 12, 19, 0, 0, TimeSpan.Zero);
        var plan = new LegendaryEventPlan
        {
            Id = LegendaryEventPlanId.From(Guid.CreateVersion7()),
            ProfileId = database.ProfileId,
            EventId = "votanUthar",
            CatalogVersion = "test",
            Notes = "notes",
            Teams = LegendaryEventValidation.LaneIds
                .SelectMany(lane => Enumerable.Range(0, 2).Select(order => new LegendaryEventTeamRecord
                {
                    Id = LegendaryEventTeamId.From(Guid.CreateVersion7()),
                    LaneId = lane,
                    Name = $"{lane} {order}",
                    SortOrder = order,
                    MemberUnitIds = [$"{lane}{order}a", $"{lane}{order}b", $"{lane}{order}c"],
                    ReserveUnitId = order == 0 ? $"{lane}{order}r" : null,
                    ObjectiveIndexes = [0, 3],
                    RunDepths =
                    [
                        new() { Run = 1, ExpectedBattleClears = 9, Source = LegendaryEventDepthSource.Manual, RecordedAt = recordedAt },
                        new() { Run = 2, ExpectedBattleClears = 7, Source = LegendaryEventDepthSource.Estimate, RecordedAt = recordedAt },
                    ],
                }))
                .ToList(),
            Runs =
            [
                new() { Run = 1, RegularMissions = 3, PremiumMissions = 2, CompletedRegularMissions = [0, 2], BundlePurchased = true, CloseShards = 25, UpdatedAt = recordedAt },
            ],
            Annotations =
            [
                new() { LaneId = "beta", BattleIndex = 4, ObjectiveId = 2, Status = LegendaryEventAnnotationStatus.StopHere, UpdatedAt = recordedAt },
            ],
        };
        LegendaryEventValidation.AssertInvariants(plan);
        await using (var db = database.CreateContext())
        {
            db.LegendaryEventPlans.Add(plan);
            await db.SaveChangesAsync(ct);
        }

        await using var verify = database.CreateContext();
        var stored = await verify.LegendaryEventPlans.AsNoTracking().SingleAsync(ct);

        Assert.Equal(6, stored.Teams.Count);
        Assert.Equivalent(plan.Teams, stored.Teams, strict: true);
        Assert.Equivalent(plan.Runs, stored.Runs, strict: true);
        Assert.Equivalent(plan.Annotations, stored.Annotations, strict: true);
        Assert.Equal(1, stored.Revision);
    }

    [Fact]
    public async Task ARunRecordStoresItsRosterInline()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var record = new LegendaryEventRunRecord
        {
            Id = LegendaryEventRunRecordId.From(Guid.CreateVersion7()),
            ProfileId = database.ProfileId,
            EventId = "votanUthar",
            Run = 2,
            StartedAt = DateTimeOffset.UtcNow,
            ProgressAt = DateTimeOffset.UtcNow,
            CatalogVersion = "test",
            Roster = new RosterSnapshot
            {
                TakenAt = new DateTimeOffset(2026, 10, 12, 19, 0, 0, TimeSpan.Zero),
                CatalogVersion = "test",
                Characters =
                [
                    new PlayerCharacterRecord
                    {
                        UnitId = UnitId.From("ultraCalgar"),
                        Rank = UnitRank.Diamond1,
                        XpLevel = 45,
                        Abilities = [new() { AbilityId = "active", Level = 40 }],
                        EquippedItems = [new() { SlotId = "slot1", EquipmentId = "item", Level = 3 }],
                    },
                ],
                Mows = [new PlayerMowRecord { UnitId = UnitId.From("forgefiend"), Abilities = [new() { AbilityId = "primary", Level = 10 }] }],
            },
        };
        await using (var db = database.CreateContext())
        {
            db.LegendaryEventRunRecords.Add(record);
            await db.SaveChangesAsync(ct);
        }

        await using var verify = database.CreateContext();
        var roster = await verify.LegendaryEventRunRecords.AsNoTracking().Select(entity => entity.Roster).SingleAsync(ct);

        Assert.Equivalent(record.Roster, roster, strict: true);
    }
}
