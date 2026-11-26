using System.Reflection;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using TacticusPlanner.Api.Features.AccountManagement;

namespace TacticusPlanner.Api.Tests;

public sealed class ExternalIdentityDeletionJobTests
{
    private const string ObjectId = "e9118cad-56aa-4117-a734-8bd5778b1d82";

    [Fact]
    public async Task CompletesWhenTheUserIsDeleted()
    {
        var deleter = new StubDeleter(result: true);

        await CreateJob(deleter).RunAsync(ObjectId, TestContext.Current.CancellationToken);

        Assert.Equal([ObjectId], deleter.Requests);
    }

    [Fact]
    public async Task ThrowsWhenTheUserCouldNotBeDeletedSoHangfireRetries()
    {
        var deleter = new StubDeleter(result: false);

        var exception = await Assert.ThrowsAsync<ExternalIdentityDeletionFailedException>(
            () => CreateJob(deleter).RunAsync(ObjectId, TestContext.Current.CancellationToken));

        Assert.Contains(ObjectId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RetriesABoundedNumberOfTimesAndThenStaysFailed()
    {
        var retry = typeof(ExternalIdentityDeletionJob)
            .GetMethod(nameof(ExternalIdentityDeletionJob.RunAsync))!
            .GetCustomAttribute<AutomaticRetryAttribute>();

        Assert.NotNull(retry);
        Assert.Equal(ExternalIdentityDeletionJob.Attempts, retry.Attempts);
        Assert.Equal(AttemptsExceededAction.Fail, retry.OnAttemptsExceeded);
    }

    private static ExternalIdentityDeletionJob CreateJob(IExternalIdentityDeleter deleter) =>
        new(deleter, NullLogger<ExternalIdentityDeletionJob>.Instance);

    private sealed class StubDeleter(bool result) : IExternalIdentityDeleter
    {
        public List<string?> Requests { get; } = [];

        public Task<bool> TryDeleteAsync(string? objectId, CancellationToken cancellationToken)
        {
            Requests.Add(objectId);
            return Task.FromResult(result);
        }
    }
}
