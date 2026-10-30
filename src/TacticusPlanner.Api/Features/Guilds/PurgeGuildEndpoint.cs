using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.UpstreamRateLimiting;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Permanently deletes the caller's guild registration from Planner (Guild Phase 1). Requires a configured
/// Tacticus User ID and a linked, persisted Leader/Co-Leader role, then re-verifies that role against a FRESH
/// upstream roster fetched with the guild's stored token (via <see cref="GuildSyncService"/>, which also
/// refreshes the roster) — never authorized from the persisted role alone, since the raid history it deletes
/// cannot be restored. Fails closed: if Tacticus rejects the stored token or is unavailable, nothing is
/// deleted (400/503, as for sync); a current leader can re-register with a fresh token first. Deleting the
/// <see cref="Guild"/> row cascades to all its <see cref="GuildMember"/> rows (see
/// GuildMemberConfiguration); any member can re-register the same Tacticus guild afterward.
/// </summary>
public sealed class PurgeGuildEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("guild");
        Options(builder => builder.WithMetadata(UpstreamRateLimit.Slow));
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes the caller's guild registration and roster from Planner.";
            summary.Description = "Requires a configured Tacticus User ID and the caller to be a linked Leader or "
                + "Co-Leader of a registered guild, re-verified against a fresh Tacticus guild response fetched with "
                + "the stored Guild API token. Nothing is deleted when that fetch fails. "
                + "Deletes the guild and all of its member rows from Planner. This does not affect the guild in "
                + "Tacticus itself, only Planner's registration of it, and cannot be undone — any current member "
                + "can re-register it afterward.";
            summary.Response(StatusCodes.Status204NoContent, "The guild and its roster were deleted.");
            summary.Response(
                StatusCodes.Status400BadRequest,
                "The Tacticus API rejected the stored Guild API token, or returned malformed guild data."
            );
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status429TooManyRequests, UpstreamRateLimit.ResponseDescription);
            summary.Response(
                StatusCodes.Status403Forbidden,
                "No Tacticus User ID, no linked/registered guild or stored token, or the caller is not currently "
                    + "Leader/Co-Leader in the fresh Tacticus roster."
            );
            summary.Response(
                StatusCodes.Status404NotFound,
                "The authenticated account/profile has not been provisioned."
            );
            summary.Response(
                StatusCodes.Status409Conflict,
                "A database uniqueness conflict while refreshing the roster."
            );
            summary.Response(StatusCodes.Status503ServiceUnavailable, "The Tacticus API is currently unavailable.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();

        var tacticusUserId = await db.Profiles
            .AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => entity.TacticusUserId)
            .FirstOrDefaultAsync(ct);

        if (tacticusUserId is null)
        {
            AddError("A Tacticus User ID must be configured to delete this guild's Planner registration.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        // Unique on profile_id: a profile is on at most one guild's roster.
        var membership = await db.GuildMembers
            .AsNoTracking()
            .Where(entity => entity.ProfileId == profileId)
            .Select(entity => new { entity.GuildId, entity.Role, entity.Guild!.GuildApiToken })
            .SingleOrDefaultAsync(ct);

        if (membership is null)
        {
            AddError("No registered guild is linked to this profile.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        // Cheap pre-check so plain members never spend the guild token; the fresh roster below decides.
        if (membership.Role is not (GuildRole.Leader or GuildRole.CoLeader))
        {
            AddError("Only the guild's Leader or Co-Leader can delete its Planner registration.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        if (membership.GuildApiToken is not { } storedToken)
        {
            AddError("No Guild API token is stored for this guild, so the caller's role cannot be re-verified.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        // Synchronizing first both re-verifies the caller against the fresh roster and is the only authority:
        // any non-success (not Leader/Co-Leader upstream, token rejected, Tacticus unavailable) deletes nothing.
        var result = await Resolve<GuildSyncService>()
            .SynchronizeAsync(profileId, tacticusUserId.Value, storedToken, persistToken: false, ct: ct);

        if (result is not GuildSyncResult.Success success)
        {
            AddError(GuildSyncResultMapper.GetMessage(result));
            await Send.ErrorsAsync(GuildSyncResultMapper.GetStatusCode(result), ct);
            return;
        }

        // The stored token now answers for a different guild than the one the caller is linked to.
        if (success.Guild.Id != membership.GuildId)
        {
            AddError("Only the guild's Leader or Co-Leader can delete its Planner registration.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        db.Guilds.Remove(success.Guild);
        await db.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
