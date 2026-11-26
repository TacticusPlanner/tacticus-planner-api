using Hangfire;

namespace TacticusPlanner.Api.Features.AccountManagement;

/// <summary>
/// Retries the Entra External ID user deletion that <see cref="PurgeAccountEndpoint"/> attempted inline and could
/// not complete (Graph down, throttled, or a token failure). Each failed attempt throws, so Hangfire retries it
/// with its growing delay (about two hours across all attempts); a job that runs out of attempts stays failed in
/// the <c>hangfire</c> schema and its error is in the logs. Graph's 404 counts as done, so a retry after an
/// attempt that actually succeeded is harmless.
/// </summary>
public sealed partial class ExternalIdentityDeletionJob(
    IExternalIdentityDeleter deleter,
    ILogger<ExternalIdentityDeletionJob> logger
)
{
    public const int Attempts = 8;

    /// <param name="objectId">The Entra user's object id (the <c>oid</c> claim), already checked to be a GUID.</param>
    /// <param name="cancellationToken">Hangfire substitutes the server's shutdown token.</param>
    [AutomaticRetry(Attempts = Attempts, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task RunAsync(string objectId, CancellationToken cancellationToken)
    {
        if (!await deleter.TryDeleteAsync(objectId, cancellationToken))
        {
            throw new ExternalIdentityDeletionFailedException(objectId);
        }

        LogDeleted(logger, objectId);
    }

    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Information,
        Message = "Entra External ID user {ObjectId} deleted by the background retry."
    )]
    private static partial void LogDeleted(ILogger logger, string objectId);
}

public sealed class ExternalIdentityDeletionFailedException(string objectId)
    : Exception($"Entra External ID user {objectId} could not be deleted; Hangfire retries the job.");
