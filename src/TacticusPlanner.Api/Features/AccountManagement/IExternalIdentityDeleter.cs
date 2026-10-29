namespace TacticusPlanner.Api.Features.AccountManagement;

/// <summary>
/// Seam for deleting the corresponding external identity (Microsoft Entra External ID user) when an account is
/// purged. Implementations must not throw for an ordinary "cannot delete" outcome — return <c>false</c> instead,
/// since application-side data deletion is authoritative and must complete regardless of this result.
/// </summary>
public interface IExternalIdentityDeleter
{
    /// <param name="objectId">The Entra user's object id: the access token's <c>oid</c> claim.</param>
    Task<bool> TryDeleteAsync(string? objectId, CancellationToken cancellationToken);
}

/// <summary>
/// Used when <see cref="ExternalIdentityOptions"/> is not configured (local development without a Graph
/// credential, OpenAPI generation, tests): logs and leaves the Entra user in place. Application-side purge
/// (<see cref="PurgeAccountEndpoint"/>) proceeds and completes independently. Production uses
/// <see cref="GraphExternalIdentityDeleter"/>.
/// </summary>
public sealed partial class NoOpExternalIdentityDeleter(ILogger<NoOpExternalIdentityDeleter> logger)
    : IExternalIdentityDeleter
{
    public Task<bool> TryDeleteAsync(string? objectId, CancellationToken cancellationToken)
    {
        LogEntraDeletionSkipped(logger);
        return Task.FromResult(false);
    }

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Information,
        Message = "Skipped Entra External ID deletion: ExternalIdentity is not configured. "
            + "Application-side data was purged regardless."
    )]
    private static partial void LogEntraDeletionSkipped(ILogger logger);
}
