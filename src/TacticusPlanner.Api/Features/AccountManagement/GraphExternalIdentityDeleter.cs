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
    ILogger<GraphExternalIdentityDeleter> logger
) : IExternalIdentityDeleter
{
    public const string HttpClientName = "MicrosoftGraph";

    private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];

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

            // 404 means the user is already gone (a repeated purge), which is the outcome we want.
            if (!await SendDeleteAsync($"v1.0/users/{userId}", token.Token, cancellationToken))
            {
                return false;
            }

            // Soft-deleted users keep their email reserved for 30 days; removing them from deleted items frees it.
            return !permanentlyDelete
                || await SendDeleteAsync($"v1.0/directory/deletedItems/{userId}", token.Token, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDeletionFailed(logger, exception, userId);
            return false;
        }
    }

    private async Task<bool> SendDeleteAsync(string path, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        LogGraphRejected(logger, path, (int)response.StatusCode);
        return false;
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
