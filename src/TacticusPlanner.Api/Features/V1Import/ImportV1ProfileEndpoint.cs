using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusIntegrationEntity = TacticusPlanner.Persistence.Users.TacticusIntegration;

namespace TacticusPlanner.Api.Features.V1Import;

public sealed class ImportV1ProfileEndpoint : Endpoint<ImportV1ProfileRequest, ImportV1ProfileResponse>
{
    public override void Configure()
    {
        Post("me/v1-import");
        Summary(summary =>
        {
            summary.Summary = "Imports the Tacticus API key and user id from a V1 planner profile.";
            summary.Description = "Uses the supplied V1 username and password only to acquire a short-lived V1 "
                + "access token and read the V1 profile. The V1 credentials are never persisted; only the "
                + "personal Tacticus API key and Tacticus user id are imported into V2.";
            summary.Response<ImportV1ProfileResponse>(
                StatusCodes.Status200OK,
                "The imported Tacticus integration summary."
            );
            summary.Response(
                StatusCodes.Status400BadRequest,
                "Invalid V1 credentials, no Tacticus API key on the V1 profile, or the imported key failed validation."
            );
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden, "The authenticated user cannot access the API.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated user has not signed up yet.");
        });
    }

    public override async Task HandleAsync(ImportV1ProfileRequest req, CancellationToken ct)
    {
        var issuer = User.FindFirstValue("iss");
        var subject = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var username = req.Username?.Trim();
        var password = req.Password;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            AddError(request => request.Username, "The V1 username and password are required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var account = await db.Accounts
            .Include(entity => entity.Profile)
            .ThenInclude(entity => entity!.TacticusIntegration)
            .SingleOrDefaultAsync(entity => entity.Issuer == issuer && entity.Subject == subject, ct);

        if (account?.Profile is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var v1Client = Resolve<ITacticusV1Client>();

        // The V1 username/password live only in this local scope: they are used once to acquire a V1 access
        // token and are never written to the database or logs.
        var v1AccessToken = await v1Client.LoginAsync(username, password, ct);
        if (v1AccessToken is null)
        {
            AddError(request => request.Password, "The V1 username or password is invalid.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var v1Profile = await v1Client.GetProfileAsync(v1AccessToken, ct);
        if (v1Profile?.TacticusApiKey is not { } tacticusApiKey)
        {
            AddError(request => request.Username, "The V1 profile does not have a Tacticus API key configured.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var validation = await Resolve<TacticusApiKeyValidator>().ValidateAsync(tacticusApiKey, ct);
        if (validation is null)
        {
            AddError(request => request.Username, "The imported Tacticus API key could not be validated.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var integration = account.Profile.TacticusIntegration;
        if (integration is null)
        {
            integration = new TacticusIntegrationEntity { Id = account.Profile.Id };
            db.TacticusIntegrations.Add(integration);
        }

        var now = Resolve<TimeProvider>().GetUtcNow();
        integration.TacticusApiKey = tacticusApiKey;
        integration.TacticusSyncLastAttemptedAt = now;
        integration.TacticusSyncLastSucceededAt = now;

        if (v1Profile.TacticusUserId is { } tacticusUserId)
        {
            account.Profile.TacticusUserId = tacticusUserId;
            account.Profile.TacticusUserIdHash = Resolve<IColumnHashService>().ComputeHash(tacticusUserId);
        }

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new ImportV1ProfileResponse(
            account.Profile.Id.Value,
            validation.PlayerName,
            validation.PowerLevel,
            SecretMasker.Mask(integration.TacticusApiKey),
            SecretMasker.Mask(account.Profile.TacticusUserId)
        ), ct);
    }
}

public sealed record ImportV1ProfileRequest(string? Username, string? Password);

public sealed record ImportV1ProfileResponse(
    Guid ProfileId,
    string PlayerName,
    int PowerLevel,
    string? TacticusApiKeyMasked,
    string? TacticusUserIdMasked
);
