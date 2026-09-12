using FastEndpoints;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

public sealed class GetMyGuildRaidStatusEndpoint : EndpointWithoutRequest<GuildRaidStatusResponse>
{
    public override void Configure()
    {
        Get("guilds/me/raid-status");
        Summary(summary =>
        {
            summary.Summary = "Gets the caller's guild's current Guild Raid status.";
            summary.Description = "Returns an id-only normalized status from the latest persisted observation. This "
                + "endpoint never calls the upstream Guild Raid API; use POST guilds/me/raid-status/refresh to force a sync.";
            summary.Response<GuildRaidStatusResponse>(StatusCodes.Status200OK, "Current Guild Raid status.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated profile has not been provisioned.");
            summary.Response(StatusCodes.Status409Conflict, "Guild access, synchronization, token, or a prior observation is not ready.");
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
                await ConflictAsync(notReady.Message, ct);
                return;
            default:
                throw new InvalidOperationException("Unexpected guild readiness result.");
        }

        var result = await Resolve<GuildRaidStatusService>().GetAsync(guild, ct);
        switch (result)
        {
            case GuildRaidRefreshResult.Success success:
                await Send.OkAsync(success.Response, ct);
                break;
            case GuildRaidRefreshResult.NeverObserved:
                await ConflictAsync("The linked guild has no Guild Raid status observation yet.", ct);
                break;
        }
    }

    private async Task ConflictAsync(string message, CancellationToken ct)
    {
        AddError(message);
        await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
    }
}
