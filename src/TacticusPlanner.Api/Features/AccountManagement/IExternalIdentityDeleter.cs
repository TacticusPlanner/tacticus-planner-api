namespace TacticusPlanner.Api.Features.AccountManagement;

/// <summary>
/// Seam for deleting the corresponding external identity (Microsoft Entra External ID user) when an account is
/// purged. Implementations must not throw for an ordinary "cannot delete" outcome — return <c>false</c> instead,
/// since application-side data deletion is authoritative and must complete regardless of this result.
/// </summary>
public interface IExternalIdentityDeleter
{
    Task<bool> TryDeleteAsync(string issuer, string subject, CancellationToken cancellationToken);
}

/// <summary>
/// Default, documented no-op: the API's current Entra app registration does not hold Microsoft Graph
/// <c>User.ReadWrite.All</c> (or an admin-consented equivalent), so Entra External ID deletion cannot be
/// performed yet. Application-side purge (<see cref="TacticusPlanner.Api.Features.AccountManagement.PurgeAccountEndpoint"/>)
/// proceeds and completes independently of this limitation. Replace this implementation with a Microsoft Graph
/// client once the required permission and admin consent are granted.
/// </summary>
public sealed partial class NoOpExternalIdentityDeleter(ILogger<NoOpExternalIdentityDeleter> logger)
    : IExternalIdentityDeleter
{
    public Task<bool> TryDeleteAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        LogEntraDeletionSkipped(logger, issuer);
        return Task.FromResult(false);
    }

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Information,
        Message = "Skipped Entra External ID deletion for issuer {Issuer}: no Microsoft Graph deletion permission "
            + "is configured. Application-side data was purged regardless."
    )]
    private static partial void LogEntraDeletionSkipped(ILogger logger, string issuer);
}
