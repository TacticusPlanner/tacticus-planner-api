using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TacticusPlanner.Api.Features.FeatureFlags;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusPlanner.Persistence.FeatureGates;

namespace TacticusPlanner.Api.Tests;

public sealed class SchemaFeatureGateTests(GameCatalogApiFactory factory) : IClassFixture<GameCatalogApiFactory>
{
    private const string BattleProgressMigration = "20261030225953_AddBattleProgressOverrides";

    [Fact]
    public void GatedFlagsAreReadFromTheMigrations() =>
        Assert.Contains(SchemaFeatureFlags.ManualBattles, SchemaFeatureGate.GatedFlags);

    [Fact]
    public void AnOffFlagSkipsItsMigrationAndUnmapsItsColumn()
    {
        using var db = CreateContext(new SchemaFeatureGate([SchemaFeatureFlags.ManualBattles]));

        Assert.DoesNotContain(BattleProgressMigration, db.GetService<IMigrationsAssembly>().Migrations.Keys);
        var overrides = db.Model.FindEntityType(typeof(PlayerDataOverride))!;
        Assert.Null(overrides.FindNavigation(nameof(PlayerDataOverride.BattleProgressOverrides)));
        Assert.NotNull(overrides.FindNavigation(nameof(PlayerDataOverride.OnslaughtProgressOverrides)));
    }

    [Fact]
    public void AnOnFlagKeepsItsMigrationAndColumn()
    {
        using var db = CreateContext(SchemaFeatureGate.AllEnabled);

        Assert.Contains(BattleProgressMigration, db.GetService<IMigrationsAssembly>().Migrations.Keys);
        Assert.NotNull(db.Model.FindEntityType(typeof(PlayerDataOverride))!
            .FindNavigation(nameof(PlayerDataOverride.BattleProgressOverrides)));
    }

    [Fact]
    public void ContextsWithDifferentGatesDoNotShareAModel()
    {
        using var on = CreateContext(SchemaFeatureGate.AllEnabled);
        using var off = CreateContext(new SchemaFeatureGate([SchemaFeatureFlags.ManualBattles]));

        Assert.NotSame(on.Model, off.Model);
    }

    [Fact]
    public async Task ASchemaFlagTurnedOnAfterStartupStaysHidden()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"FeatureManagement:{FeatureFlags.ManualBattles}", "true");
            // As if the flag was off when the process started and turned on since.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<SchemaFeatureGate>();
                services.AddSingleton(new SchemaFeatureGate([FeatureFlags.ManualBattles]));
            });
        });
        var client = host.CreateClient();

        Assert.DoesNotContain(FeatureFlags.ManualBattles, (await GetEnabledFlagsAsync(client, ct)).Enabled);
        var battleProgress = await client.GetAsync("/api/v1/player-data-overrides/battle-progress", ct);
        Assert.Equal(HttpStatusCode.NotFound, battleProgress.StatusCode);
    }

    [Fact]
    public async Task ASchemaFlagOnAtStartupIsListed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting($"FeatureManagement:{FeatureFlags.ManualBattles}", "true"));

        Assert.Contains(FeatureFlags.ManualBattles, (await GetEnabledFlagsAsync(host.CreateClient(), ct)).Enabled);
    }

    private static async Task<FeatureFlagsResponse> GetEnabledFlagsAsync(HttpClient client, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/feature-flags");
        request.Headers.Add(TestAuthenticationHandler.NoAuthHeader, "1");
        var response = await client.SendAsync(request, ct);
        return (await response.Content.ReadFromJsonAsync<FeatureFlagsResponse>(ct))!;
    }

    private static PlannerDbContext CreateContext(SchemaFeatureGate gate)
    {
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .UseSnakeCaseNamingConvention();
        options.UseSchemaFeatureGate(gate);
        return new PlannerDbContext(options.Options, new PassthroughEncryption(), new NoProfile());
    }

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
