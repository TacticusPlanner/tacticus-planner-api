using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Accounts;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

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
        var state = ProcessorState<CurrentUserState>();
        var db = Resolve<PlannerDbContext>();

        var account = state.AccountId is { } accountId
            ? await FindAccountAsync(db, accountId, ct)
            : null;

        if (account?.Profile is null)
        {
            account = await ProvisionAccountAsync(db, state.Issuer, state.Subject, User, ct);
        }

        account.LastSeenAt = Resolve<TimeProvider>().GetUtcNow();
        await db.SaveChangesAsync(ct);

        var profile = account.Profile!;
        var tacticusApiKey = profile.TacticusIntegration?.TacticusApiKey;

        await Send.OkAsync(new CurrentUserResponse(
            account.Id.Value,
            profile.DisplayName,
            tacticusApiKey is not null,
            SecretMasker.Mask(tacticusApiKey),
            SecretMasker.Mask(profile.TacticusUserId?.Value)
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
        var profileId = ProfileId.From(Guid.CreateVersion7());
        var defaultProject = new Project
        {
            Id = ProjectId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            Name = "My Goals",
            Status = ProjectStatus.Active,
            IsDefault = true,
        };

        var account = new Account
        {
            Id = AccountId.From(Guid.CreateVersion7()),
            Issuer = issuer,
            Subject = subject,
            Profile = new Profile
            {
                Id = profileId,
                DisplayName = GetDisplayName(user),
                ActiveProjectId = defaultProject.Id,
            },
        };

        db.Accounts.Add(account);
        db.Projects.Add(defaultProject);

        await db.SaveChangesAsync(ct);

        return await FindAccountAsync(db, account.Id, ct)
            ?? throw new InvalidOperationException("Account provisioning failed unexpectedly.");
    }

    private static Task<Account?> FindAccountAsync(PlannerDbContext db, AccountId accountId, CancellationToken ct)
    {
        return db.Accounts
            .Include(account => account.Profile)
            .ThenInclude(profile => profile!.TacticusIntegration)
            .FirstOrDefaultAsync(account => account.Id == accountId, ct);
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
