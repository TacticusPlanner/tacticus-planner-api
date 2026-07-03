using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusIntegrationEntity = TacticusPlanner.Persistence.Users.TacticusIntegration;

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
        var tacticusApiKey = Normalize(req.TacticusApiKey);
        if (tacticusApiKey is null)
        {
            AddError(request => request.TacticusApiKey, "The Tacticus API key is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var validation = await Resolve<TacticusApiKeyValidator>().ValidateAsync(tacticusApiKey, ct);
        if (validation is null)
        {
            AddError(request => request.TacticusApiKey, "The Tacticus API key could not be validated.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var issuer = User.FindFirstValue("iss");
        var subject = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var account = await db.Accounts
            .Include(entity => entity.Profile)
            .ThenInclude(entity => entity!.TacticusIntegration)
            .SingleOrDefaultAsync(
                entity => entity.Issuer == issuer && entity.Subject == subject,
                ct
            );

        if (account?.Profile is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var tacticusUserId = Normalize(req.TacticusUserId);
        account.Profile.TacticusUserId = tacticusUserId;
        account.Profile.TacticusUserIdHash = Resolve<IColumnHashService>().ComputeHash(tacticusUserId);

        var integration = account.Profile.TacticusIntegration;
        if (integration is null)
        {
            integration = new TacticusIntegrationEntity
            {
                Id = account.Profile.Id,
            };

            db.TacticusIntegrations.Add(integration);
        }

        integration.TacticusApiKey = tacticusApiKey;

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new UpdateTacticusIntegrationResponse(
            account.Profile.Id.Value,
            tacticusUserId is not null,
            true,
            validation.PlayerName,
            validation.PowerLevel
        ), ct);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record UpdateTacticusIntegrationRequest(
    string? TacticusUserId,
    string? TacticusApiKey
);

public sealed record UpdateTacticusIntegrationResponse(
    Guid ProfileId,
    bool TacticusUserIdConfigured,
    bool TacticusApiKeyConfigured,
    string PlayerName,
    int PowerLevel
);
