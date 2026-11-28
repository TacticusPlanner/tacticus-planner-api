namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Maps a <see cref="GuildSyncResult"/> failure branch to the HTTP status and message the Guild Phase 1
/// spec calls for. The success branch is handled by each endpoint directly, since only it knows how to
/// build the 200 OK response body.
/// </summary>
internal static class GuildSyncResultMapper
{
    public static int GetStatusCode(GuildSyncResult result)
    {
        return result switch
        {
            GuildSyncResult.InvalidRequest => StatusCodes.Status400BadRequest,
            GuildSyncResult.UpstreamRejected => StatusCodes.Status400BadRequest,
            GuildSyncResult.InvalidUpstreamData => StatusCodes.Status400BadRequest,
            GuildSyncResult.UpstreamUnavailable => StatusCodes.Status503ServiceUnavailable,
            GuildSyncResult.CallerNotAuthorized => StatusCodes.Status403Forbidden,
            GuildSyncResult.Conflict => StatusCodes.Status409Conflict,
            GuildSyncResult.VerificationRequired => StatusCodes.Status403Forbidden,
            GuildSyncResult.Success => StatusCodes.Status200OK,
            _ => StatusCodes.Status500InternalServerError,
        };
    }

    public static string GetMessage(GuildSyncResult result)
    {
        return result switch
        {
            GuildSyncResult.InvalidRequest invalidRequest => invalidRequest.Message,
            GuildSyncResult.UpstreamRejected upstreamRejected => upstreamRejected.Message,
            GuildSyncResult.InvalidUpstreamData invalidUpstreamData => invalidUpstreamData.Message,
            GuildSyncResult.UpstreamUnavailable upstreamUnavailable => upstreamUnavailable.Message,
            GuildSyncResult.CallerNotAuthorized callerNotAuthorized => callerNotAuthorized.Message,
            GuildSyncResult.Conflict conflict => conflict.Message,
            GuildSyncResult.VerificationRequired verificationRequired => verificationRequired.Message,
            _ => "The request failed.",
        };
    }

    /// <summary>The issue code a failure carries, when it has one.</summary>
    public static string? GetIssueCode(GuildSyncResult result) =>
        result is GuildSyncResult.VerificationRequired ? GuildSyncResult.VerificationRequired.IssueCode : null;
}
