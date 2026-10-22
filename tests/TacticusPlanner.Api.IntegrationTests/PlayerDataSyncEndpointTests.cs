using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.PlayerData;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi.Models.Player;

namespace TacticusPlanner.Api.IntegrationTests;

public sealed class PlayerDataSyncEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task UnauthenticatedRequestIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.PostAsync(
            "/api/v1/player-data/sync",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnprovisionedAccountIsNotFound()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, NewSubject());

        var response = await client.PostAsync(
            "/api/v1/player-data/sync",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoTacticusApiKeyConfiguredIsRejected()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsync(
            "/api/v1/player-data/sync",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SuccessfulSyncReturnsAManifestWithEveryChunk()
    {
        var client = await CreateProvisionedClientAsync();
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);

        var response = await client.PostAsync(
            "/api/v1/player-data/sync",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var manifest = await response.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);
        Assert.NotNull(manifest);
        Assert.Equal(FakeTacticusApi.ConfigHashV1, manifest.GameConfigHash);
        Assert.NotEmpty(manifest.SourceHash);
        Assert.Equal(PlayerDataChunkKeys.All.Count, manifest.Chunks.Count);
        Assert.All(manifest.Chunks, chunk => Assert.NotEmpty(chunk.Hash));
        Assert.Contains(manifest.Chunks, chunk => chunk.Key == PlayerDataChunkKeys.Characters);
    }

    [Fact]
    public async Task IdenticalRepeatedResponseRetainsChunkHashesAndAdvancesSuccessfulSyncMetadata()
    {
        var subject = NewSubject();
        var client = await CreateProvisionedClientAsync(subject);
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);

        var first = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var firstManifest = await first.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);
        var firstState = await GetStoredStateAsync(subject);

        await Task.Delay(10, TestContext.Current.CancellationToken);

        var second = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var secondManifest = await second.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);
        var secondState = await GetStoredStateAsync(subject);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstManifest!.SourceHash, secondManifest!.SourceHash);
        Assert.Equal(firstState.ChunkHashes, secondState.ChunkHashes);
        Assert.True(secondManifest.SyncedAt > firstManifest.SyncedAt);
        Assert.True(secondState.LastSucceededAt > firstState.LastSucceededAt);
    }

    [Fact]
    public async Task LockedCharacterShardChangesUnderSameConfigHashUpdateOnlyInventoryShards()
    {
        var subject = NewSubject();
        var token = $"same-config-shards-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse(
            lastUpdatedOn: FakeTacticusApi.LastUpdatedOnV1,
            lockedCharacterShards: 285));

        var client = await CreateProvisionedClientAsync(subject);
        await ConfigureTacticusKeyAsync(client, token);
        var first = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var firstManifest = await first.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);

        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse(
            lastUpdatedOn: FakeTacticusApi.LastUpdatedOnV1 + 60,
            lockedCharacterShards: 298));
        var second = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var secondManifest = await second.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);
        var stored = await GetStoredStateAsync(subject);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(FakeTacticusApi.ConfigHashV1, secondManifest!.GameConfigHash);
        Assert.Equal(298, stored.LockedCharacterShards);
        AssertOnlyChunkChanged(firstManifest!, secondManifest, PlayerDataChunkKeys.InventoryShards);
    }

    [Fact]
    public async Task RosterProgressionChangesUnderSameConfigHashUpdateOnlyCharacters()
    {
        var token = $"same-config-roster-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse());

        var client = await CreateProvisionedClientAsync();
        await ConfigureTacticusKeyAsync(client, token);
        var first = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var firstManifest = await first.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);

        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse(
            lastUpdatedOn: FakeTacticusApi.LastUpdatedOnV1 + 60,
            progressionIndex: 12,
            xp: 210000,
            rank: 13));
        var second = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var secondManifest = await second.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(FakeTacticusApi.ConfigHashV1, secondManifest!.GameConfigHash);
        AssertOnlyChunkChanged(firstManifest!, secondManifest, PlayerDataChunkKeys.Characters);
    }

    [Fact]
    public async Task UpstreamFreshnessAdvancesIndependentlyFromConfigAndContentHashes()
    {
        var subject = NewSubject();
        var token = $"freshness-only-{Guid.NewGuid()}";
        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse());

        var client = await CreateProvisionedClientAsync(subject);
        await ConfigureTacticusKeyAsync(client, token);
        var first = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var firstManifest = await first.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);

        factory.TacticusApi.ConfigurePlayerResponse(token, FakeTacticusApi.BuildPlayerResponse(
            lastUpdatedOn: FakeTacticusApi.LastUpdatedOnV1 + 120));
        var second = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var secondManifest = await second.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);
        var stored = await GetStoredStateAsync(subject);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstManifest!.GameConfigHash, secondManifest!.GameConfigHash);
        Assert.Equal(firstManifest.SourceHash, secondManifest.SourceHash);
        Assert.Equal(
            firstManifest.Chunks.ToDictionary(chunk => chunk.Key, chunk => chunk.Hash),
            secondManifest.Chunks.ToDictionary(chunk => chunk.Key, chunk => chunk.Hash));
    }

    [Fact]
    public async Task SyncWithChangedConfigHashUpdatesTheStoredSnapshot()
    {
        var subject = NewSubject();
        var client = await CreateProvisionedClientAsync(subject);
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);

        var first = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var firstManifest = await first.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);
        var revisionAfterFirst = await GetRevisionAsync(subject);

        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKeyV2);
        var second = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var secondManifest = await second.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(FakeTacticusApi.ConfigHashV2, secondManifest!.GameConfigHash);
        Assert.NotEqual(firstManifest!.SourceHash, secondManifest.SourceHash);

        var revisionAfterSecond = await GetRevisionAsync(subject);
        Assert.True(revisionAfterSecond > revisionAfterFirst);
    }

    [Fact]
    public async Task SyncWithChangedConfigHashOnlyChangesTheChunksThatActuallyDiffer()
    {
        // FakeTacticusApi's V1/V2 responses only differ in the synced unit's progression/xp/rank — every
        // other chunk (details, inventory, campaigns, live progress, LREs) is byte-identical between the
        // two. Asserts the sync only rewrites the Characters chunk hash, proving the per-chunk changed-hash
        // check (not a blanket rewrite of all ten chunks) is what actually drives persistence.
        var client = await CreateProvisionedClientAsync();
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);

        var first = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var firstManifest = await first.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);

        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKeyV2);
        var second = await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);
        var secondManifest = await second.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstHashes = firstManifest!.Chunks.ToDictionary(chunk => chunk.Key, chunk => chunk.Hash);
        var secondHashes = secondManifest!.Chunks.ToDictionary(chunk => chunk.Key, chunk => chunk.Hash);

        Assert.NotEqual(firstHashes[PlayerDataChunkKeys.Characters], secondHashes[PlayerDataChunkKeys.Characters]);

        foreach (var key in PlayerDataChunkKeys.All.Where(key => key != PlayerDataChunkKeys.Characters))
        {
            Assert.Equal(firstHashes[key], secondHashes[key]);
        }
    }

    [Fact]
    public async Task ChunkEndpointRejectsUnknownKeysAndServesKnownOnesWithConditionalSupport()
    {
        var client = await CreateProvisionedClientAsync();
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);
        await client.PostAsync("/api/v1/player-data/sync", null, TestContext.Current.CancellationToken);

        // Unknown chunk key is now a validator rejection (malformed request), not a missing-resource 404.
        var unknown = await client.GetAsync("/api/v1/player-data/not-a-real-chunk", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var chunkResponse = await client.GetAsync(
            $"/api/v1/player-data/{PlayerDataChunkKeys.Characters}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, chunkResponse.StatusCode);

        var envelope = await chunkResponse.Content
            .ReadFromJsonAsync<PlayerDataChunkEnvelope<object>>(TestContext.Current.CancellationToken);
        Assert.NotNull(envelope);
        Assert.Equal(PlayerDataChunkKeys.Characters, envelope.ChunkKey);
        Assert.NotEmpty(envelope.ChunkHash);

        var etag = chunkResponse.Headers.ETag?.Tag;
        Assert.NotNull(etag);

        using var conditionalRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/player-data/{PlayerDataChunkKeys.Characters}");
        conditionalRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var notModified = await client.SendAsync(conditionalRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    private const string SyncPath = "/api/v1/player-data/sync";

    private static PlayerResponse ContentVariant(int rank, int xp = 200000) =>
        FakeTacticusApi.BuildPlayerResponse(progressionIndex: 11, xp: xp, rank: rank);

    [Fact]
    public async Task ChangedChunksAreWrittenEvenWhenTheConfigHashIsUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = NewSubject();
        var client = await CreateProvisionedClientAsync(subject);
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);
        try
        {
            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, ContentVariant(rank: 12));
            var first = await (await client.PostAsync(SyncPath, null, ct)).Content.ReadFromJsonAsync<PlayerDataManifest>(ct);
            var revision = await GetRevisionAsync(subject);
            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, ContentVariant(rank: 14, xp: 250000));

            var second = await (await client.PostAsync(SyncPath, null, ct)).Content.ReadFromJsonAsync<PlayerDataManifest>(ct);

            Assert.Equal(first!.GameConfigHash, second!.GameConfigHash);
            AssertOnlyChunkChanged(first, second, PlayerDataChunkKeys.Characters);
            Assert.Equal(revision + 1, await GetRevisionAsync(subject));
            Assert.Equal(
                second.Chunks.Single(chunk => chunk.Key == PlayerDataChunkKeys.Characters).Hash,
                (await GetStoredStateAsync(subject)).ChunkHashes[PlayerDataChunkKeys.Characters]);
            Assert.Equal(14, await StoredRankAsync(subject));
        }
        finally
        {
            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, FakeTacticusApi.BuildPlayerResponse());
        }
    }

    [Fact]
    public async Task SlowSyncRacingANewerCommitIsRejectedWithoutOverwritingOrPartialRecords()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = NewSubject();
        var client = await CreateProvisionedClientAsync(subject);
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);
        try
        {
            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, ContentVariant(rank: 12));
            (await client.PostAsync(SyncPath, null, ct)).EnsureSuccessStatusCode();
            var revision = await GetRevisionAsync(subject);

            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, ContentVariant(rank: 13));
            var hold = factory.TacticusApi.HoldNextPlayerCall(FakeTacticusApi.ValidKey);
            var slow = client.PostAsync(SyncPath, null, ct);
            await hold.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, ContentVariant(rank: 14));
            var winner = await client.PostAsync(SyncPath, null, ct);
            winner.EnsureSuccessStatusCode();
            var winnerManifest = await winner.Content.ReadFromJsonAsync<PlayerDataManifest>(ct);
            hold.Release.SetResult();
            var loser = await slow;

            Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            Assert.Equal(revision + 1, await GetRevisionAsync(subject));
            Assert.Equal(14, await StoredRankAsync(subject));
            var stored = await GetStoredStateAsync(subject);
            Assert.Equal(winnerManifest!.SyncedAt, stored.LastSucceededAt);
            Assert.Equal(
                winnerManifest.Chunks.Single(chunk => chunk.Key == PlayerDataChunkKeys.Characters).Hash,
                stored.ChunkHashes[PlayerDataChunkKeys.Characters]);
        }
        finally
        {
            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, FakeTacticusApi.BuildPlayerResponse());
        }
    }

    [Fact]
    public async Task RacingFirstInsertsWithDifferentContentLeaveOneWinnerAndAConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = NewSubject();
        var client = await CreateProvisionedClientAsync(subject);
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);
        try
        {
            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, ContentVariant(rank: 13));
            var hold = factory.TacticusApi.HoldNextPlayerCall(FakeTacticusApi.ValidKey);
            var slow = client.PostAsync(SyncPath, null, ct);
            await hold.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, ContentVariant(rank: 12));
            (await client.PostAsync(SyncPath, null, ct)).EnsureSuccessStatusCode();
            var revision = await GetRevisionAsync(subject);
            hold.Release.SetResult();
            var loser = await slow;

            Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            Assert.Equal(revision, await GetRevisionAsync(subject));
            Assert.Equal(12, await StoredRankAsync(subject));
        }
        finally
        {
            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, FakeTacticusApi.BuildPlayerResponse());
        }
    }

    [Fact]
    public async Task RacingFirstInsertsWithIdenticalContentBothSucceed()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = NewSubject();
        var client = await CreateProvisionedClientAsync(subject);
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);
        try
        {
            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, ContentVariant(rank: 12));
            var hold = factory.TacticusApi.HoldNextPlayerCall(FakeTacticusApi.ValidKey);
            var slow = client.PostAsync(SyncPath, null, ct);
            await hold.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            (await client.PostAsync(SyncPath, null, ct)).EnsureSuccessStatusCode();
            var revision = await GetRevisionAsync(subject);
            hold.Release.SetResult();

            var converged = await slow;

            Assert.Equal(HttpStatusCode.OK, converged.StatusCode);
            Assert.Equal(revision + 1, await GetRevisionAsync(subject));
            Assert.Equal(12, await StoredRankAsync(subject));
        }
        finally
        {
            factory.TacticusApi.ConfigurePlayerResponse(FakeTacticusApi.ValidKey, FakeTacticusApi.BuildPlayerResponse());
        }
    }

    private async Task<int> StoredRankAsync(string subject)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var snapshot = await db.PlayerDataSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(entity => entity.Profile!.Subject == subject, TestContext.Current.CancellationToken);
        return (int)snapshot.Characters.Single().Rank;
    }

    private async Task<long> GetRevisionAsync(string subject)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        // IgnoreQueryFilters: this scope has no HttpContext, so PlannerDbContext's global profile query
        // filter (see ApplyProfileQueryFilters) has no current profile to scope to.
        var snapshot = await db.PlayerDataSnapshots
            .IgnoreQueryFilters()
            .Include(entity => entity.Profile)
            .SingleAsync(
                entity => entity.Profile!.Subject == subject,
                TestContext.Current.CancellationToken);

        return snapshot.Revision;
    }

    private async Task<StoredSyncState> GetStoredStateAsync(string subject)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
        var snapshot = await db.PlayerDataSnapshots
            .IgnoreQueryFilters()
            .Include(entity => entity.Profile)
            .SingleAsync(
                entity => entity.Profile!.Subject == subject,
                TestContext.Current.CancellationToken);
        var lastSucceededAt = await db.TacticusIntegrations
            .IgnoreQueryFilters()
            .Where(entity => entity.Id == snapshot.Id)
            .Select(entity => entity.TacticusSyncLastSucceededAt)
            .SingleAsync(TestContext.Current.CancellationToken);

        return new StoredSyncState(
            new Dictionary<string, string>(snapshot.ChunkHashes, StringComparer.Ordinal),
            snapshot.InventoryShards.SingleOrDefault(shard => shard.UnitId.Value == FakeTacticusApi.LockedCharacterUnitId)?.Amount,
            lastSucceededAt);
    }

    private static void AssertOnlyChunkChanged(
        PlayerDataManifest first,
        PlayerDataManifest second,
        string changedChunk)
    {
        var firstHashes = first.Chunks.ToDictionary(chunk => chunk.Key, chunk => chunk.Hash);
        var secondHashes = second.Chunks.ToDictionary(chunk => chunk.Key, chunk => chunk.Hash);

        Assert.NotEqual(firstHashes[changedChunk], secondHashes[changedChunk]);
        foreach (var key in PlayerDataChunkKeys.All.Where(key => key != changedChunk))
        {
            Assert.Equal(firstHashes[key], secondHashes[key]);
        }
    }

    private async Task<HttpClient> CreateProvisionedClientAsync(string? subject = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, subject ?? NewSubject());

        // GET /account auto-provisions the Account + Profile that the sync endpoint requires to exist.
        await client.GetAsync("/api/v1/account", TestContext.Current.CancellationToken);

        return client;
    }

    private static async Task ConfigureTacticusKeyAsync(HttpClient client, string apiKey)
    {
        var response = await client.PutAsJsonAsync(
            "/api/v1/account/tacticus-integration",
            new UpdateTacticusIntegrationRequest(apiKey, null),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static string NewSubject() => $"player-data-sync-{Guid.NewGuid()}";

    private sealed record StoredSyncState(
        IReadOnlyDictionary<string, string> ChunkHashes,
        long? LockedCharacterShards,
        DateTimeOffset? LastSucceededAt);
}
