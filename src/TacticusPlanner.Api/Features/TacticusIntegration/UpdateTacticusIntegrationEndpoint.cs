using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusIntegrationEntity = TacticusPlanner.Domain.Profiles.TacticusIntegration;

namespace TacticusPlanner.Api.Features.TacticusIntegration;

public sealed class UpdateTacticusIntegrationEndpoint
    : Endpoint<UpdateTacticusIntegrationRequest, UpdateTacticusIntegrationResponse>
{
    public override void Configure()
    {
        Put("me/tacticus-integration");
        Summary(summary =>
        {
            summary.Summary = "Updates the authenticated user's Tacticus integration settings.";
            summary.Description = "The Tacticus API key is only revalidated and updated when a new value is "
                + "supplied; omit it to change only the Tacticus user id. The user id can be updated or removed "
                + "independently — set clearTacticusUserId to remove it without touching the stored API key.";
            summary.Response<UpdateTacticusIntegrationResponse>(
                StatusCodes.Status200OK,
                "The saved Tacticus integration summary."
            );
            summary.Response(StatusCodes.Status400BadRequest, "The Tacticus API key is missing or invalid.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden, "The authenticated user cannot access the API.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated user has not signed up yet.");
        });
    }

    public override async Task HandleAsync(UpdateTacticusIntegrationRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var profile = await db.Profiles
            .Include(entity => entity.TacticusIntegration)
            .FirstOrDefaultAsync(entity => entity.Id == profileId, ct);

        if (profile is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var integration = profile.TacticusIntegration;
        var newApiKey = Normalize(req.TacticusApiKey);

        string? playerName = null;
        int? powerLevel = null;

        if (newApiKey is not null)
        {
            var validation = await Resolve<TacticusApiKeyValidator>().ValidateAsync(newApiKey, ct);
            if (validation is null)
            {
                AddError(request => request.TacticusApiKey, "The Tacticus API key could not be validated.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            }

            playerName = validation.PlayerName;
            powerLevel = validation.PowerLevel;

            if (integration is null)
            {
                integration = new TacticusIntegrationEntity { Id = profile.Id };
                db.TacticusIntegrations.Add(integration);
            }

            var now = Resolve<TimeProvider>().GetUtcNow();
            integration.TacticusApiKey = newApiKey;
            integration.TacticusSyncLastAttemptedAt = now;
            integration.TacticusSyncLastSucceededAt = now;
        }
        else if (integration?.TacticusApiKey is null)
        {
            AddError(request => request.TacticusApiKey, "The Tacticus API key is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        if (req.ClearTacticusUserId)
        {
            profile.TacticusUserId = null;
            profile.TacticusUserIdHash = null;
        }
        else if (Normalize(req.TacticusUserId) is { } tacticusUserId)
        {
            profile.TacticusUserId = TacticusUserId.From(tacticusUserId);
            profile.TacticusUserIdHash = Resolve<IColumnHashService>().ComputeHash(tacticusUserId);
        }

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new UpdateTacticusIntegrationResponse(
            profile.Id.Value,
            profile.TacticusUserId is not null,
            integration?.TacticusApiKey is not null,
            playerName,
            powerLevel,
            SecretMasker.Mask(integration?.TacticusApiKey),
            SecretMasker.Mask(profile.TacticusUserId?.Value)
        ), ct);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record UpdateTacticusIntegrationRequest(
    string? TacticusApiKey,
    string? TacticusUserId,
    bool ClearTacticusUserId = false
);

public sealed record UpdateTacticusIntegrationResponse(
    Guid ProfileId,
    bool TacticusUserIdConfigured,
    bool TacticusApiKeyConfigured,
    string? PlayerName,
    int? PowerLevel,
    string? TacticusApiKeyMasked,
    string? TacticusUserIdMasked
);
