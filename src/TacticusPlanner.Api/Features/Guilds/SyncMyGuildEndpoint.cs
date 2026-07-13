using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Explicitly re-synchronizes the authenticated user's guild (Guild Phase 1). Requires the caller to be a
/// linked member of a registered guild with a stored token, and to still be Leader/Co-Leader in the fresh
/// upstream roster — a demoted or removed caller is rejected and nothing is persisted (never authorize from
/// the stale persisted role).
/// </summary>
public sealed class SyncMyGuildEndpoint : EndpointWithoutRequest<RegisteredGuildResponse>
{
    public override void Configure()
    {
        Post("guilds/me/sync");
        Summary(summary =>
        {
            summary.Summary = "Re-synchronizes the authenticated user's guild from the Tacticus API.";
            summary.Description = "Fetches fresh guild data using the stored Guild API token and requires "
                + "the caller to still appear as Leader or Co-Leader in that fresh response before any data "
                + "changes. The stored token itself is never replaced by this endpoint.";
            summary.Response<RegisteredGuildResponse>(StatusCodes.Status200OK, "The refreshed guild projection.");
            summary.Response(StatusCodes.Status400BadRequest, "The upstream guild response was malformed.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(
                StatusCodes.Status403Forbidden,
                "No linked/registered guild, no stored token, or the caller is no longer Leader/Co-Leader."
            );
            summary.Response(
                StatusCodes.Status404NotFound,
                "The authenticated account/profile has not been provisioned."
            );
            summary.Response(
                StatusCodes.Status409Conflict,
                "A database uniqueness conflict with an existing guild or membership."
            );
            summary.Response(StatusCodes.Status503ServiceUnavailable, "The Tacticus API is currently unavailable.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();

        var profile = await db.Profiles
            .AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => new { entity.TacticusUserId })
            .FirstOrDefaultAsync(ct);

        if (profile is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (profile.TacticusUserId is null)
        {
            AddError("A Tacticus User ID must be configured to synchronize this guild.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var membership = await db.GuildMembers
            .AsNoTracking()
            .Where(entity => entity.ProfileId == profileId)
            .Select(entity => new { entity.GuildId })
            .FirstOrDefaultAsync(ct);

        if (membership is null)
        {
            AddError("No registered guild is linked to this profile.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var storedToken = await db.Guilds
            .AsNoTracking()
            .Where(entity => entity.Id == membership.GuildId)
            .Select(entity => entity.GuildApiToken)
            .FirstOrDefaultAsync(ct);

        if (storedToken is null)
        {
            AddError("No Guild API token is stored for this guild.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var result = await Resolve<GuildSyncService>()
            .SynchronizeAsync(profileId, profile.TacticusUserId.Value, storedToken, persistToken: false, ct);

        if (result is GuildSyncResult.Success success)
        {
            await Send.OkAsync(GuildProjection.Build(success.Guild, success.CallerMember), ct);
            return;
        }

        AddError(GuildSyncResultMapper.GetMessage(result));
        await Send.ErrorsAsync(GuildSyncResultMapper.GetStatusCode(result), ct);
    }
}
