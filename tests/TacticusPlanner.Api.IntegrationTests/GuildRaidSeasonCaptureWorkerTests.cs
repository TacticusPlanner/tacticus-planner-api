using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using UpstreamGuildRole = TacticusPlanner.TacticusApi.Models.Guild.GuildRole;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>The real worker behind a refresh.</summary>
public sealed class GuildRaidSeasonCaptureWorkerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RefreshAnswersBeforeCaptureFinishesAndCaptureCompletesAfterwards()
    {
        await using var factory = new PlannerApiFactory { RunSeasonCaptureWorker = true };
        var gate = new TaskCompletionSource<GuildRaidResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (client, token, tag) = await RegisterAsync(factory);
        factory.TacticusApi.ConfigureGuildRaidSeason(token, 113, () => gate.Task);

        (await client.PostAsync("/api/v1/guild/raid-status/refresh", null, Ct)).EnsureSuccessStatusCode();
        await WaitAsync(() => Task.FromResult(factory.TacticusApi.GuildRaidSeasonCalls(token).Count == 1));
        Assert.False(await IsFinalAsync(factory, tag));

        gate.SetResult(new GuildRaidResponse { Season = 113, SeasonConfigId = "guild_boss_season_config_1" });
        await WaitAsync(() => IsFinalAsync(factory, tag));
    }

    [Fact]
    public async Task CrashedRunLeavesRefreshSuccessfulAndTheWorkerRunning()
    {
        await using var factory = new PlannerApiFactory { RunSeasonCaptureWorker = true };
        var (client, token, tag) = await RegisterAsync(factory);
        factory.TacticusApi.ConfigureGuildRaidSeason(
            token, new GuildRaidResponse { Season = 113, SeasonConfigId = "guild_boss_season_config_1" });

        const string refreshPath = "/api/v1/guild/raid-status/refresh";
        (await client.PostAsync(refreshPath, null, Ct)).EnsureSuccessStatusCode();
        await WaitAsync(() => IsFinalAsync(factory, tag));
        await WaitAsync(async () => (await StateAsync(factory, tag)).CaptureNotBefore is null);

        // The list is applied, so the next run starts with the missing table, outside its own failure handling.
        await ExecuteAsync(factory, db => db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE guild_raid_season_captures RENAME TO captures_away", Ct));
        await ExpireCooldownAsync(factory);
        (await client.PostAsync(refreshPath, null, Ct)).EnsureSuccessStatusCode();
        var requests = factory.Services.GetRequiredService<GuildRaidSeasonCaptureRequests>();
        await WaitAsync(() => Task.FromResult(requests.Reader.Count == 0));
        await Task.Delay(500, Ct);

        await ExecuteAsync(factory, db => db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE captures_away RENAME TO guild_raid_season_captures", Ct));
        await ExecuteAsync(factory, db => db.GuildRaidSeasonCaptures.ExecuteUpdateAsync(
            set => set.SetProperty(capture => capture.Status, GuildRaidSeasonCaptureStatus.Pending), Ct));
        await ExpireCooldownAsync(factory);
        (await client.PostAsync("/api/v1/guild/raid-status/refresh", null, Ct)).EnsureSuccessStatusCode();
        await WaitAsync(() => IsFinalAsync(factory, tag));
    }

    private static async Task<(HttpClient Client, string Token, string Tag)> RegisterAsync(PlannerApiFactory factory)
    {
        var (client, tacticusUserId) = await GuildTestHelpers.CreateGuildReadyClientAsync(factory);
        var token = $"worker-token-{Guid.NewGuid()}";
        var tag = $"W{Guid.NewGuid():N}"[..10];
        var upstream = FakeTacticusApi.BuildGuildResponse(
            Guid.NewGuid(), tag, "Worker Guild", 10, (tacticusUserId, UpstreamGuildRole.LEADER, 50, null));
        upstream.Guild.GuildRaidSeasons = [113, 114];
        factory.TacticusApi.ConfigureGuildResponse(token, upstream);
        (await client.PostAsJsonAsync("/api/v1/guild/register", new RegisterGuildRequest(token), Ct)).EnsureSuccessStatusCode();
        factory.TacticusApi.ConfigureGuildRaidResponse(token, new GuildRaidResponse
        {
            Season = 114,
            SeasonConfigId = factory.Services.GetRequiredService<IGameCatalogProvider>().Current.RaidBossesView.Seasons.Keys.Min()!,
        });
        return (client, token, tag);
    }

    private static async Task ExecuteAsync(PlannerApiFactory factory, Func<PlannerDbContext, Task> work)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await work(scope.ServiceProvider.GetRequiredService<PlannerDbContext>());
    }

    private static Task ExpireCooldownAsync(PlannerApiFactory factory)
    {
        var expired = DateTimeOffset.UtcNow.AddMinutes(-2);
        return ExecuteAsync(factory, db => db.GuildRaidSyncStates.ExecuteUpdateAsync(
            set => set.SetProperty(state => state.LastAttemptedAt, expired), Ct));
    }

    private static async Task<bool> IsFinalAsync(PlannerApiFactory factory, string tag)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PlannerDbContext>().GuildRaidSeasonCaptures.AnyAsync(
            capture => capture.Guild!.Tag == tag && capture.Status == GuildRaidSeasonCaptureStatus.Final, Ct);
    }

    private static async Task<Domain.GuildRaids.GuildRaidSyncState> StateAsync(PlannerApiFactory factory, string tag)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PlannerDbContext>().GuildRaidSyncStates.AsNoTracking()
            .SingleAsync(state => state.Guild!.Tag == tag, Ct);
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        Assert.Fail("The condition was not met in ten seconds.");
    }
}
