using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TacticusPlanner.Api.Features.PlayerData;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Tests;

public sealed class PlayerDataSyncEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    [Fact]
    public async Task UnauthenticatedRequestIsRejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");

        var response = await client.PostAsync(
            "/api/v1/tacticus-integration/player-sync",
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
            "/api/v1/tacticus-integration/player-sync",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NoTacticusApiKeyConfiguredIsRejected()
    {
        var client = await CreateProvisionedClientAsync();

        var response = await client.PostAsync(
            "/api/v1/tacticus-integration/player-sync",
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
            "/api/v1/tacticus-integration/player-sync",
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
    public async Task RepeatedSyncWithUnchangedConfigHashAdvancesSuccessfulSyncTimestamp()
    {
        var subject = NewSubject();
        var client = await CreateProvisionedClientAsync(subject);
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);

        var first = await client.PostAsync("/api/v1/tacticus-integration/player-sync", null, TestContext.Current.CancellationToken);
        var firstManifest = await first.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);

        await Task.Delay(10, TestContext.Current.CancellationToken);

        var second = await client.PostAsync("/api/v1/tacticus-integration/player-sync", null, TestContext.Current.CancellationToken);
        var secondManifest = await second.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstManifest!.SourceHash, secondManifest!.SourceHash);
        Assert.True(secondManifest.SyncedAt > firstManifest.SyncedAt);
    }

    [Fact]
    public async Task SyncWithChangedConfigHashUpdatesTheStoredSnapshot()
    {
        var subject = NewSubject();
        var client = await CreateProvisionedClientAsync(subject);
        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKey);

        var first = await client.PostAsync("/api/v1/tacticus-integration/player-sync", null, TestContext.Current.CancellationToken);
        var firstManifest = await first.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);
        var revisionAfterFirst = await GetRevisionAsync(subject);

        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKeyV2);
        var second = await client.PostAsync("/api/v1/tacticus-integration/player-sync", null, TestContext.Current.CancellationToken);
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

        var first = await client.PostAsync("/api/v1/tacticus-integration/player-sync", null, TestContext.Current.CancellationToken);
        var firstManifest = await first.Content.ReadFromJsonAsync<PlayerDataManifest>(TestContext.Current.CancellationToken);

        await ConfigureTacticusKeyAsync(client, FakeTacticusApi.ValidKeyV2);
        var second = await client.PostAsync("/api/v1/tacticus-integration/player-sync", null, TestContext.Current.CancellationToken);
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
        await client.PostAsync("/api/v1/tacticus-integration/player-sync", null, TestContext.Current.CancellationToken);

        // Unknown chunk key is now a validator rejection (malformed request), not a missing-resource 404.
        var unknown = await client.GetAsync("/api/v1/me/player-data/not-a-real-chunk", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var chunkResponse = await client.GetAsync(
            $"/api/v1/me/player-data/{PlayerDataChunkKeys.Characters}",
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
            $"/api/v1/me/player-data/{PlayerDataChunkKeys.Characters}");
        conditionalRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var notModified = await client.SendAsync(conditionalRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
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
            .ThenInclude(entity => entity!.Account)
            .SingleAsync(
                entity => entity.Profile!.Account!.Subject == subject,
                TestContext.Current.CancellationToken);

        return snapshot.Revision;
    }

    private async Task<HttpClient> CreateProvisionedClientAsync(string? subject = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, subject ?? NewSubject());

        // /me auto-provisions the Account + Profile that the sync endpoint requires to exist.
        await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        return client;
    }

    private static async Task ConfigureTacticusKeyAsync(HttpClient client, string apiKey)
    {
        var response = await client.PutAsJsonAsync(
            "/api/v1/me/tacticus-integration",
            new UpdateTacticusIntegrationRequest(apiKey, null),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static string NewSubject() => $"player-data-sync-{Guid.NewGuid()}";
}
