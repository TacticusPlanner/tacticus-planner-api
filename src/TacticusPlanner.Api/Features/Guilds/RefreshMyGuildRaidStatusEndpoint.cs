using FastEndpoints;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

public sealed class RefreshMyGuildRaidStatusEndpoint : EndpointWithoutRequest<GuildRaidStatusResponse>
{
    public override void Configure()
    {
        Post("guilds/me/raid-status/refresh");
        Summary(summary =>
        {
            summary.Summary = "Forces a Guild Raid status refresh for the caller's guild.";
            summary.Description = "Performs an upstream Guild Raid sync subject to a one-minute per-guild cooldown "
                + "measured from the last attempt, successful or failed; a request inside the cooldown returns the "
                + "current persisted result instead of calling upstream.";
            summary.Response<GuildRaidStatusResponse>(StatusCodes.Status200OK, "Current Guild Raid status after refresh.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated profile has not been provisioned.");
            summary.Response(StatusCodes.Status409Conflict, "Guild access, synchronization, or token is not ready.");
            summary.Response(StatusCodes.Status502BadGateway, "The upstream credential or response was rejected.");
            summary.Response(StatusCodes.Status503ServiceUnavailable, "The upstream service is unavailable and no retained status exists.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        Guild guild;
        switch (await ReadyGuildResolver.ResolveAsync(db, profileId, ct))
        {
            case ReadyGuildResult.Ready ready:
                guild = ready.Guild;
                break;
            case ReadyGuildResult.ProfileNotFound:
                await Send.NotFoundAsync(ct);
                return;
            case ReadyGuildResult.NotReady notReady:
                AddError(notReady.Message);
                await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
                return;
            default:
                throw new InvalidOperationException("Unexpected guild readiness result.");
        }

        var result = await Resolve<GuildRaidStatusService>().RefreshAsync(guild, ct);
        switch (result)
        {
            case GuildRaidRefreshResult.Success success:
                await Send.OkAsync(success.Response, ct);
                break;
            case GuildRaidRefreshResult.Rejected rejected:
                AddError(rejected.Message);
                await Send.ErrorsAsync(StatusCodes.Status502BadGateway, ct);
                break;
            case GuildRaidRefreshResult.Unavailable unavailable:
                AddError(unavailable.Message);
                await Send.ErrorsAsync(StatusCodes.Status503ServiceUnavailable, ct);
                break;
        }
    }
}
