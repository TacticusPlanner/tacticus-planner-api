using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Gets the authenticated user's guild registration/membership state. Reads persisted state only — this
/// endpoint never calls the Tacticus API (see the Guild Phase 1 spec's "current user's guild" contract).
/// </summary>
public sealed class GetMyGuildEndpoint : EndpointWithoutRequest<MyGuildResponse>
{
    public override void Configure()
    {
        Get("guilds/me");
        Summary(summary =>
        {
            summary.Summary = "Gets the authenticated user's guild registration and membership state.";
            summary.Description = "The state field discriminates between needing a Tacticus User ID "
                + "(tacticusUserIdRequired), having no linked registered guild (unregistered), and full "
                + "guild + member details (registered). This endpoint reads persisted state only.";
            summary.Response<MyGuildResponse>(StatusCodes.Status200OK, "The caller's guild state.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated account/profile has not been provisioned.");
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
            .Select(entity => new { entity.TacticusUserIdHash })
            .FirstOrDefaultAsync(ct);

        if (profile is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (profile.TacticusUserIdHash is null)
        {
            await Send.OkAsync(new MyGuildResponse(GuildStateValues.TacticusUserIdRequired, null), ct);
            return;
        }

        var member = await db.GuildMembers
            .AsNoTracking()
            .Include(entity => entity.Guild)
            .ThenInclude(guild => guild!.Members)
            .FirstOrDefaultAsync(entity => entity.ProfileId == profileId, ct);

        if (member?.Guild is null)
        {
            await Send.OkAsync(new MyGuildResponse(GuildStateValues.Unregistered, null), ct);
            return;
        }

        await Send.OkAsync(
            new MyGuildResponse(GuildStateValues.Registered, GuildProjection.Build(member.Guild, member)),
            ct
        );
    }
}

public static class GuildStateValues
{
    public const string TacticusUserIdRequired = "tacticusUserIdRequired";
    public const string Unregistered = "unregistered";
    public const string Registered = "registered";
}

public sealed record MyGuildResponse(string State, RegisteredGuildResponse? Guild);
