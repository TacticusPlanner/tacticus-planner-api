using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Registers a Tacticus guild for the authenticated Leader or Co-Leader (Guild Phase 1). Validates the
/// token against the Tacticus API, requires the caller to appear as Leader/Co-Leader in the fresh response,
/// then runs the shared <see cref="GuildSyncService"/> and persists the encrypted token.
/// </summary>
public sealed class RegisterGuildEndpoint : Endpoint<RegisterGuildRequest, RegisteredGuildResponse>
{
    public override void Configure()
    {
        Post("guilds/register");
        Summary(summary =>
        {
            summary.Summary = "Registers a Tacticus guild using a Guild-scoped API token.";
            summary.Description = "Requires the caller to have a configured Tacticus User ID and to appear "
                + "as the guild's Leader or Co-Leader in the upstream response. Performs a complete "
                + "synchronization and persists the encrypted token. Re-registering an already-registered "
                + "guild updates it rather than duplicating it.";
            summary.Response<RegisteredGuildResponse>(StatusCodes.Status200OK, "The newly registered guild.");
            summary.Response(
                StatusCodes.Status400BadRequest,
                "Blank/invalid token, malformed upstream guild response, or no Tacticus User ID configured."
            );
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden, "The caller is absent from the guild or below Co-Leader.");
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

    public override async Task HandleAsync(RegisterGuildRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var token = req.GuildApiToken?.Trim() ?? string.Empty;
        if (token.Length == 0)
        {
            AddError(request => request.GuildApiToken, "A Guild API token is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
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
            AddError("A Tacticus User ID must be configured before registering a guild.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var result = await Resolve<GuildSyncService>()
            .SynchronizeAsync(profileId, profile.TacticusUserId.Value, token, persistToken: true, ct);

        if (result is GuildSyncResult.Success success)
        {
            await Send.OkAsync(GuildProjection.Build(success.Guild, success.CallerMember), ct);
            return;
        }

        AddError(GuildSyncResultMapper.GetMessage(result));
        await Send.ErrorsAsync(GuildSyncResultMapper.GetStatusCode(result), ct);
    }
}

public sealed record RegisterGuildRequest(string GuildApiToken);
