using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        }

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var delete = new NpgsqlCommand("DELETE FROM profiles WHERE id = @id", connection);
            delete.Parameters.AddWithValue("id", database.ProfileId.Value);
            await delete.ExecuteNonQueryAsync(ct);
        }

        await using var verify = database.CreateContext();
        Assert.Empty(await verify.Teams.IgnoreQueryFilters().ToListAsync(ct));
    }
}
