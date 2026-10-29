using System.Net;
using System.Net.Http.Headers;
using Azure.Core;

namespace TacticusPlanner.Api.Features.AccountManagement;

/// <summary>
/// Deletes the Entra External ID user through Microsoft Graph with an app-only token. Graph addresses users by
/// object id (the access token's <c>oid</c>), not by the per-application <c>sub</c> the profile is keyed on.
/// </summary>
public sealed partial class GraphExternalIdentityDeleter(
    HttpClient httpClient,
    TokenCredential credential,
    bool permanentlyDelete,
    ILogger<GraphExternalIdentityDeleter> logger,
    IReadOnlyList<TimeSpan>? deletedItemsRetryDelays = null
) : IExternalIdentityDeleter
{
    public const string HttpClientName = "MicrosoftGraph";

    private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];

    // A just-deleted user can take a moment to show up in deleted items, so a 404 there is retried a few times.
    private static readonly TimeSpan[] DefaultDeletedItemsRetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    private readonly IReadOnlyList<TimeSpan> _deletedItemsRetryDelays =
        deletedItemsRetryDelays ?? DefaultDeletedItemsRetryDelays;

    public async Task<bool> TryDeleteAsync(string? objectId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(objectId, out var userId))
        {
            LogMissingObjectId(logger);
            return false;
        }

        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(GraphScopes), cancellationToken);

            var userResult = await SendDeleteAsync($"v1.0/users/{userId}", token.Token, cancellationToken);

            // 404 means the user is already gone (a repeated purge), which is the outcome we want.
            if (userResult == DeleteResult.Failed)
            {
                return false;
            }

            if (!permanentlyDelete)
            {
                return true;
            }

            // Soft-deleted users keep their email reserved for 30 days; removing them from deleted items frees it.
            var deletedItemsPath = $"v1.0/directory/deletedItems/{userId}";
            var deletedItemsResult = await SendDeleteAsync(deletedItemsPath, token.Token, cancellationToken);

            // Only a user this call just deleted is expected to appear in deleted items, so only then is a 404 retried.
            if (userResult == DeleteResult.Deleted)
            {
                for (var attempt = 0;
                    deletedItemsResult == DeleteResult.NotFound && attempt < _deletedItemsRetryDelays.Count;
                    attempt++)
                {
                    await Task.Delay(_deletedItemsRetryDelays[attempt], cancellationToken);
                    deletedItemsResult = await SendDeleteAsync(deletedItemsPath, token.Token, cancellationToken);
                }
            }

            return deletedItemsResult != DeleteResult.Failed;
        }
        // HttpClient reports its own timeout as a cancellation, which is a Graph failure unless the caller cancelled.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogDeletionFailed(logger, exception, userId);
            return false;
        }
    }

    private async Task<DeleteResult> SendDeleteAsync(
        string path,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return DeleteResult.Deleted;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return DeleteResult.NotFound;
        }

        LogGraphRejected(logger, path, (int)response.StatusCode);
        return DeleteResult.Failed;
    }

    private enum DeleteResult
    {
        Deleted,
        NotFound,
        Failed,
    }

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Warning,
        Message = "Skipped Entra External ID deletion: the access token has no valid oid claim."
    )]
    private static partial void LogMissingObjectId(ILogger logger);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "Microsoft Graph refused {Path} with status {StatusCode}. Application-side data was purged regardless."
    )]
    private static partial void LogGraphRejected(ILogger logger, string path, int statusCode);

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Error,
        Message = "Entra External ID deletion failed for user {UserId}. Application-side data was purged regardless."
    )]
    private static partial void LogDeletionFailed(ILogger logger, Exception exception, Guid userId);
}
