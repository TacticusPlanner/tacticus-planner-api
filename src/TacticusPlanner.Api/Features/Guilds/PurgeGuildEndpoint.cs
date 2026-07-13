using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Permanently deletes the caller's guild registration from Planner (Guild Phase 1). Requires the caller
/// to be a linked Leader or Co-Leader of a registered guild — authorized from the persisted role, since
/// this only removes Planner's own record of the guild and never touches Tacticus itself. Deleting the
/// <see cref="Guild"/> row cascades to all its <see cref="GuildMember"/> rows (see
/// GuildMemberConfiguration); any member can re-register the same Tacticus guild afterward.
/// </summary>
public sealed class PurgeGuildEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("guilds/me");
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes the caller's guild registration and roster from Planner.";
            summary.Description = "Requires the caller to be a linked Leader or Co-Leader of a registered guild. "
                + "Deletes the guild and all of its member rows from Planner. This does not affect the guild in "
                + "Tacticus itself, only Planner's registration of it, and cannot be undone — any current member "
                + "can re-register it afterward.";
            summary.Response(StatusCodes.Status204NoContent, "The guild and its roster were deleted.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(
                StatusCodes.Status403Forbidden,
                "No linked/registered guild, or the caller is not Leader/Co-Leader."
            );
            summary.Response(
                StatusCodes.Status404NotFound,
                "The authenticated account/profile has not been provisioned."
            );
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

        var membership = await db.GuildMembers
            .Where(entity => entity.ProfileId == profileId)
            .Select(entity => new { entity.GuildId, entity.Role })
            .FirstOrDefaultAsync(ct);

        if (membership is null)
        {
            AddError("No registered guild is linked to this profile.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        if (membership.Role is not (GuildRole.Leader or GuildRole.CoLeader))
        {
            AddError("Only the guild's Leader or Co-Leader can delete its Planner registration.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        var guild = await db.Guilds.FirstOrDefaultAsync(entity => entity.Id == membership.GuildId, ct);
        if (guild is not null)
        {
            db.Guilds.Remove(guild);
            await db.SaveChangesAsync(ct);
        }

        await Send.NoContentAsync(ct);
    }
}
