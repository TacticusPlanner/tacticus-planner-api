using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// Syncs the authenticated profile's player data from the Tacticus API: fetches the player endpoint,
/// transforms and normalizes the response (never storing it raw — ADR 0007), and persists it as a
/// player-data snapshot via <see cref="PlayerDataSyncService"/> (also used by
/// <c>ImportV1ProfileEndpoint</c>, so a V1 import that selects goals does not depend on this endpoint
/// having run first). Every successful response is transformed and canonically hashed; only chunks
/// whose transformed content changed are replaced. A successful unchanged sync still advances the
/// snapshot's sync timestamp.
/// </summary>
public sealed class PlayerSyncEndpoint : EndpointWithoutRequest<PlayerDataManifest>
{
    public override void Configure()
    {
        Post("tacticus-integration/player-sync");
        Summary(summary =>
        {
            summary.Summary = "Syncs the authenticated user's player data from the Tacticus API.";
            summary.Description = "Fetches the current player from the Tacticus API, transforms it into "
                + "normalized chunks, and persists chunks whose canonical content hash changed. The game "
                + "configuration hash is retained as metadata and does not suppress player-content updates.";
            summary.Response<PlayerDataManifest>(StatusCodes.Status200OK, "The current player-data manifest.");
            summary.Response(StatusCodes.Status400BadRequest, "No Tacticus API key is configured, or the Tacticus API rejected it.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated user has not signed up yet.");
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

        // TacticusIntegration is keyed 1:1 by the same ProfileId, so this is a direct primary-key lookup —
        // no Account/Profile join, and the (potentially large) PlayerDataSnapshot row isn't touched yet.
        var apiKey = await db.TacticusIntegrations
            .Where(entity => entity.Id == profileId)
            .Select(entity => entity.TacticusApiKey)
            .FirstOrDefaultAsync(ct);
        if (apiKey is null)
        {
            AddError("No Tacticus API key is configured for this profile.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var result = await Resolve<PlayerDataSyncService>().SyncAsync(profileId, apiKey, ct);
        if (result is not PlayerDataSyncResult.Success success)
        {
            AddError("The Tacticus API could not fetch player data for the configured key.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        await Send.OkAsync(PlayerDataManifestBuilder.Build(success.Snapshot), ct);
    }
}
