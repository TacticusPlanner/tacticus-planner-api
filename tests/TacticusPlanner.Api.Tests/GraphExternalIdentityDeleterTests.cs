using System.Net;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using TacticusPlanner.Api.Features.AccountManagement;

namespace TacticusPlanner.Api.Tests;

public class GraphExternalIdentityDeleterTests
{
    private static readonly Guid UserId = Guid.Parse("e9118cad-56aa-4117-a734-8bd5778b1d82");

    [Fact]
    public async Task DeletesTheUserAndThenItsDeletedItemsEntry()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent, HttpStatusCode.NoContent);

        var deleted = await CreateDeleter(handler, permanentlyDelete: true)
            .TryDeleteAsync(UserId.ToString(), TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Equal(
            [$"DELETE /v1.0/users/{UserId}", $"DELETE /v1.0/directory/deletedItems/{UserId}"],
            handler.Requests
        );
        Assert.All(handler.Authorizations, value => Assert.Equal("Bearer graph-token", value));
    }

    [Fact]
    public async Task LeavesTheSoftDeletedUserWhenPermanentDeletionIsOff()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent);

        var deleted = await CreateDeleter(handler, permanentlyDelete: false)
            .TryDeleteAsync(UserId.ToString(), TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Equal([$"DELETE /v1.0/users/{UserId}"], handler.Requests);
    }

    [Fact]
    public async Task TreatsAnAlreadyDeletedUserAsSuccess()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, HttpStatusCode.NotFound);

        var deleted = await CreateDeleter(handler, permanentlyDelete: true)
            .TryDeleteAsync(UserId.ToString(), TestContext.Current.CancellationToken);

        Assert.True(deleted);
    }

    [Fact]
    public async Task ReportsFailureWithoutThrowingWhenGraphRefuses()
    {
        var handler = new RecordingHandler(HttpStatusCode.Forbidden);

        var deleted = await CreateDeleter(handler, permanentlyDelete: true)
            .TryDeleteAsync(UserId.ToString(), TestContext.Current.CancellationToken);

        Assert.False(deleted);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SkipsGraphWithoutAnObjectId()
    {
        var handler = new RecordingHandler();

        var deleted = await CreateDeleter(handler, permanentlyDelete: true)
            .TryDeleteAsync(null, TestContext.Current.CancellationToken);

        Assert.False(deleted);
        Assert.Empty(handler.Requests);
    }

    private static GraphExternalIdentityDeleter CreateDeleter(RecordingHandler handler, bool permanentlyDelete) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/") },
            new StaticTokenCredential(),
            permanentlyDelete,
            NullLogger<GraphExternalIdentityDeleter>.Instance
        );

    private sealed class StaticTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("graph-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class RecordingHandler(params HttpStatusCode[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<string> Requests { get; } = [];

        public List<string?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            Authorizations.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(new HttpResponseMessage(responses[_next++]));
        }
    }
}
