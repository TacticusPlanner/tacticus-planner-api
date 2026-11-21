using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using TacticusPlanner.Api.Features.Teams;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Teams;
using Xunit;

namespace TacticusPlanner.Persistence.IntegrationTests;

/// <summary>The <c>teams</c> table: one row per team with its lineup in <c>text[]</c> columns, names unique per profile
/// ignoring case, one team per V1 import key, deleted with its profile. See specs/personal-teams and
/// specs/v1-team-import.</summary>
public sealed class TeamPostgresTests
{
    private static TeamDraft Draft(string name) => new(
        name,
        [TeamMode.WarDefense, TeamMode.Horde],
        ["ultraCalgar", "ultraTitus", "bloodDante"],
        2,
        ["ultraDreadnought"],
        null,
        [],
        "  Notes  ");

    private static async Task<DbUpdateException> SaveFailsAsync(PostgresTestDatabase database, Team team, CancellationToken ct)
    {
        await using var db = database.CreateContext();
        db.Teams.Add(team);
        return await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task ATeamRoundTripsAsOneRow()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var team = Team.Create(database.ProfileId, Draft("Ultra wall"), 1, importKey: "ultra wall");
        await using (var db = database.CreateContext())
        {
            db.Teams.Add(team);
            await db.SaveChangesAsync(ct);
        }

        await using var verify = database.CreateContext();
        var stored = await verify.Teams.AsNoTracking().SingleAsync(ct);

        Assert.Equal(team.Id, stored.Id);
        Assert.Equal("ultra wall", stored.NameKey);
        Assert.Equal("ultra wall", stored.ImportKey);
        Assert.Equal([TeamMode.WarDefense, TeamMode.Horde], stored.Modes);
        Assert.Equal(["ultraCalgar", "ultraTitus", "bloodDante"], stored.CharacterIds);
        Assert.Equal(2, stored.FlexStartIndex);
        Assert.Equal(["ultraDreadnought"], stored.MowIds);
        Assert.Empty(stored.IncursionMowIds);
        Assert.Equal("Notes", stored.Notes);
        Assert.Equal(1, stored.Revision);
    }

    [Fact]
    public async Task NamesAreUniquePerProfileIgnoringCase()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var other = ProfileId.From(Guid.CreateVersion7());
        await database.SeedProfileAsync(other);
        await using (var db = database.CreateContext())
        {
            db.Teams.Add(Team.Create(database.ProfileId, Draft("Mortarion core"), 1));
            await db.SaveChangesAsync(ct);
        }

        await using (var otherDb = database.CreateContext(other))
        {
            otherDb.Teams.Add(Team.Create(other, Draft("Mortarion core"), 1));
            await otherDb.SaveChangesAsync(ct);
        }

        var error = await SaveFailsAsync(database, Team.Create(database.ProfileId, Draft("mortarion CORE"), 2), ct);
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("ux_teams_profile_id_name_key", postgres.ConstraintName);
    }

    [Fact]
    public async Task ImportKeysAreUniquePerProfileAndOptional()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await using (var db = database.CreateContext())
        {
            db.Teams.Add(Team.Create(database.ProfileId, Draft("GW Ultras"), 1, importKey: "gw ultras"));
            db.Teams.Add(Team.Create(database.ProfileId, Draft("Made in V2"), 2));
            db.Teams.Add(Team.Create(database.ProfileId, Draft("Also made in V2"), 3));
            await db.SaveChangesAsync(ct);
        }

        var error = await SaveFailsAsync(database, Team.Create(database.ProfileId, Draft("Ultra D1"), 4, importKey: "gw ultras"), ct);
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("ux_teams_profile_id_import_key", postgres.ConstraintName);
    }

    [Fact]
    public async Task ARetriedLockedUpdateStartsFromTheStoredTeam()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var team = Team.Create(database.ProfileId, Draft("Before"), 1);
        await using (var seed = database.CreateContext())
        {
            seed.Teams.Add(team);
            await seed.SaveChangesAsync(ct);
        }

        // The first commit fails transiently after its save, so the execution strategy runs the update again.
        await using var db = database.CreateContext(new FailFirstCommit());
        var service = new TeamsService(db, null!, Options.Create(new TeamOptions()));
        var attempts = 0;
        var outcome = await service.ExecuteLockedAsync(async transaction =>
        {
            attempts++;
            var stored = await db.Teams.FirstAsync(entity => entity.Id == team.Id, ct);
            if (stored.Revision != 1)
                return "stale";
            stored.Apply(Draft("After"));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return "updated";
        }, ct);

        Assert.Equal(("updated", 2), (outcome, attempts));
        await using var verify = database.CreateContext();
        var saved = await verify.Teams.AsNoTracking().SingleAsync(ct);
        Assert.Equal(("After", 2L), (saved.Name, saved.Revision));
    }

    [Fact]
    public async Task ARetryKeepsWhatTheCallerTrackedAtItsStoredValues()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var team = Team.Create(database.ProfileId, Draft("Before"), 1);
        await using (var seed = database.CreateContext())
        {
            seed.Teams.Add(team);
            await seed.SaveChangesAsync(ct);
        }

        await using var db = database.CreateContext(new FailFirstCommit());
        // Tracked by the caller before the locked write, then changed by the attempt that rolls back.
        var tracked = await db.Teams.FirstAsync(entity => entity.Id == team.Id, ct);
        var service = new TeamsService(db, null!, Options.Create(new TeamOptions()));
        var seen = new List<(string Name, EntityState State)>();
        await service.ExecuteLockedAsync(async transaction =>
        {
            seen.Add((tracked.Name, db.Entry(tracked).State));
            tracked.Apply(Draft("After"));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }, ct);

        Assert.Equal([("Before", EntityState.Unchanged), ("Before", EntityState.Unchanged)], seen);
        await using var verify = database.CreateContext();
        Assert.Equal("After", (await verify.Teams.AsNoTracking().SingleAsync(ct)).Name);
    }

    [Fact]
    public async Task ALockedWriteRefusesUnsavedChanges()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await using var db = database.CreateContext();
        db.Teams.Add(Team.Create(database.ProfileId, Draft("Pending"), 1));
        var service = new TeamsService(db, null!, Options.Create(new TeamOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteLockedAsync(_ => Task.FromResult(true), ct));
        Assert.True(db.ChangeTracker.HasChanges());
    }

    private sealed class FailFirstCommit : DbTransactionInterceptor
    {
        private bool failed;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (failed)
                return ValueTask.FromResult(result);
            failed = true;
            throw new NpgsqlException("Simulated transient commit failure.", new TimeoutException());
        }
    }

    [Fact]
    public async Task TeamsAreScopedToTheirProfileAndDeletedWithIt()
    {
        await using var database = await PostgresTestDatabase.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var other = ProfileId.From(Guid.CreateVersion7());
        await database.SeedProfileAsync(other);
        await using (var db = database.CreateContext())
        {
            db.Teams.Add(Team.Create(database.ProfileId, Draft("First"), 1));
            db.Teams.Add(Team.Create(database.ProfileId, Draft("Second"), 2));
            await db.SaveChangesAsync(ct);
        }

        await using (var otherDb = database.CreateContext(other))
        {
            Assert.Empty(await otherDb.Teams.ToListAsync(ct));
            otherDb.Teams.Add(Team.Create(other, Draft("Other's"), 1));
            await otherDb.SaveChangesAsync(ct);
        }

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var delete = new NpgsqlCommand("DELETE FROM profiles WHERE id = @id", connection);
            delete.Parameters.AddWithValue("id", database.ProfileId.Value);
            await delete.ExecuteNonQueryAsync(ct);
        }

        // Only the deleted profile's teams go; the other profile keeps its own.
        await using var verify = database.CreateContext();
        var survivor = Assert.Single(await verify.Teams.IgnoreQueryFilters().ToListAsync(ct));
        Assert.Equal((other, "Other's"), (survivor.ProfileId, survivor.Name));
    }
}
