using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Api.Persistence;
using TacticusPlanner.Api.Persistence.Encryption;
using TacticusPlanner.Api.Persistence.Users;

namespace TacticusPlanner.Api.Features.Signup;

public sealed class SignupEndpoint : Endpoint<SignupRequest, SignupResponse>
{
    public override void Configure()
    {
        Post("signup");
        Summary(summary =>
        {
            summary.Summary = "Creates the authenticated user's planner account.";
            summary.Response<SignupResponse>(
                StatusCodes.Status200OK,
                "The existing or newly-created planner account."
            );
            summary.Response(StatusCodes.Status400BadRequest, "The supplied Tacticus API key is invalid.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden, "The authenticated user cannot access the API.");
        });
    }

    public override async Task HandleAsync(SignupRequest req, CancellationToken ct)
    {
        var db = Resolve<PlannerDbContext>();
        var columnHash = Resolve<IColumnHashService>();
        var tacticusApiKeyValidator = Resolve<TacticusApiKeyValidator>();
        var issuer = User.FindFirstValue("iss");
        var subject = User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var existing = await FindAccountAsync(db, issuer, subject, ct);
        if (existing is not null)
        {
            await Send.OkAsync(ToResponse(existing, created: false), ct);
            return;
        }

        var tacticusApiKey = Normalize(req.TacticusApiKey);
        var tacticusValidation = tacticusApiKey is null
            ? null
            : await tacticusApiKeyValidator.ValidateAsync(tacticusApiKey, ct);

        if (tacticusApiKey is not null && tacticusValidation is null)
        {
            AddError(request => request.TacticusApiKey, "The Tacticus API key could not be validated.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var tacticusUserId = Normalize(req.TacticusUserId);
        var profileId = ProfileId.From(Guid.CreateVersion7());
        var account = new Account
        {
            Id = AccountId.From(Guid.CreateVersion7()),
            Issuer = issuer,
            Subject = subject,
            Profile = new Profile
            {
                Id = profileId,
                DisplayName = GetDisplayName(User),
                TacticusUserId = tacticusUserId,
                TacticusUserIdHash = columnHash.ComputeHash(tacticusUserId),
                TacticusIntegration = tacticusApiKey is null
                    ? null
                    : new Persistence.Users.TacticusIntegration
                    {
                        Id = profileId,
                        TacticusApiKey = tacticusApiKey,
                    },
            },
        };

        db.Accounts.Add(account);

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(ToResponse(account, created: true), ct);
    }

    private static Task<Account?> FindAccountAsync(
        PlannerDbContext db,
        string issuer,
        string subject,
        CancellationToken cancellationToken
    )
    {
        return db.Accounts
            .Include(account => account.Profile)
            .SingleOrDefaultAsync(
                account => account.Issuer == issuer && account.Subject == subject,
                cancellationToken
            );
    }

    private static SignupResponse ToResponse(Account account, bool created)
    {
        var profile = account.Profile
            ?? throw new InvalidOperationException("Account is missing its profile.");

        return new SignupResponse(account.Id.Value, profile.Id.Value, profile.DisplayName, created);
    }

    private static string GetDisplayName(ClaimsPrincipal user)
    {
        return user.FindFirstValue("name")
            ?? user.FindFirstValue("preferred_username")
            ?? "Planner User";
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record SignupRequest(
    string? TacticusUserId,
    string? TacticusApiKey
);

public sealed record SignupResponse(
    Guid AccountId,
    Guid ProfileId,
    string DisplayName,
    bool Created
);
