using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Users;

namespace TacticusPlanner.Api.Features.CurrentUser;

public sealed class GetCurrentUserEndpoint : EndpointWithoutRequest<CurrentUserResponse>
{
    public override void Configure()
    {
        Get("me");
        Summary(summary =>
        {
            summary.Summary = "Gets the authenticated user's planner account, creating it on first access.";
            summary.Description = "Every authenticated caller has a planner account: this endpoint creates the "
                + "Account and Profile on first access if they do not exist yet. Tacticus integration values are "
                + "never returned in full — only a masked preview and whether onboarding is complete.";
            summary.Response<CurrentUserResponse>(
                StatusCodes.Status200OK,
                "The authenticated user's account and Tacticus integration status."
            );
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden, "The authenticated user cannot access the API.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var issuer = User.FindFirstValue("iss");
        var subject = User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var account = await FindAccountAsync(db, issuer, subject, ct);

        if (account?.Profile is null)
        {
            account = await ProvisionAccountAsync(db, issuer, subject, User, ct);
        }

        var profile = account.Profile!;
        var tacticusApiKey = profile.TacticusIntegration?.TacticusApiKey;

        await Send.OkAsync(new CurrentUserResponse(
            account.Id.Value,
            profile.DisplayName,
            tacticusApiKey is not null,
            SecretMasker.Mask(tacticusApiKey),
            SecretMasker.Mask(profile.TacticusUserId)
        ), ct);
    }

    private static async Task<Account> ProvisionAccountAsync(
        PlannerDbContext db,
        string issuer,
        string subject,
        ClaimsPrincipal user,
        CancellationToken ct
    )
    {
        var account = new Account
        {
            Id = AccountId.From(Guid.CreateVersion7()),
            Issuer = issuer,
            Subject = subject,
            Profile = new Profile
            {
                Id = ProfileId.From(Guid.CreateVersion7()),
                DisplayName = GetDisplayName(user),
            },
        };

        db.Accounts.Add(account);

        await db.SaveChangesAsync(ct);

        account = await FindAccountAsync(db, issuer, subject, ct)
                  ?? throw new InvalidOperationException("Account provisioning failed unexpectedly.");

        return account;
    }

    private static Task<Account?> FindAccountAsync(
        PlannerDbContext db,
        string issuer,
        string subject,
        CancellationToken ct
    )
    {
        return db.Accounts
            .Include(account => account.Profile)
            .ThenInclude(profile => profile!.TacticusIntegration)
            .SingleOrDefaultAsync(account => account.Issuer == issuer && account.Subject == subject, ct);
    }

    private static string GetDisplayName(ClaimsPrincipal user)
    {
        return user.FindFirstValue("name")
            ?? user.FindFirstValue("preferred_username")
            ?? "Planner User";
    }
}

public sealed record CurrentUserResponse(
    Guid ApplicationUserId,
    string DisplayName,
    bool HasCompletedOnboarding,
    string? TacticusApiKeyMasked,
    string? TacticusUserIdMasked
);
