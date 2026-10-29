using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
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
        Get("guild");
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
            await this.SendNotFoundProblemAsync(ct);
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
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        if (profile.TacticusUserIdHash is null)
        {
            await Send.OkAsync(new MyGuildResponse(MyGuildState.TacticusUserIdRequired, null), ct);
            return;
        }

        var membership = await db.GuildMembers
            .AsNoTracking()
            .Where(entity => entity.ProfileId == profileId)
            .Select(entity => new { entity.Id, entity.GuildId })
            .SingleOrDefaultAsync(ct); // unique on profile_id: at most one guild per profile

        if (membership is null)
        {
            await Send.OkAsync(new MyGuildResponse(MyGuildState.Unregistered, null), ct);
            return;
        }

        // Rooted on Guild (not GuildMember) so the Include tree is a single level (Guild -> Members) rather
        // than looping back to GuildMember via Guild — EF Core rejects that as a cycle in no-tracking queries.
        var guild = await db.Guilds
            .AsNoTracking()
            .Include(entity => entity.Members)
            .FirstOrDefaultAsync(entity => entity.Id == membership.GuildId, ct);

        if (guild is null)
        {
            await Send.OkAsync(new MyGuildResponse(MyGuildState.Unregistered, null), ct);
            return;
        }

        var callerMember = guild.Members.First(entity => entity.Id == membership.Id);

        await Send.OkAsync(
            new MyGuildResponse(MyGuildState.Registered, GuildProjection.Build(guild, callerMember)),
            ct
        );
    }
}

/// <summary>Where the caller stands with guild access; the client switches its guild page on it.</summary>
public enum MyGuildState
{
    [JsonStringEnumMemberName("tacticusUserIdRequired")] TacticusUserIdRequired,
    [JsonStringEnumMemberName("unregistered")] Unregistered,
    [JsonStringEnumMemberName("registered")] Registered,
}

public sealed record MyGuildResponse(MyGuildState State, RegisteredGuildResponse? Guild);
