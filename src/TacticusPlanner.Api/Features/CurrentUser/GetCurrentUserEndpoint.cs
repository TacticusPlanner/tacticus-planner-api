using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Analytics;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Accounts;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.CurrentUser;

public sealed class GetCurrentUserEndpoint : EndpointWithoutRequest<CurrentUserResponse>
{
    /// <summary>Stored until the user sets a name; a chosen name is always 1-80 characters, so this can never
    /// collide with one. Provider claims are never persisted as the name.</summary>
    public const string NoDisplayName = "";

    public override void Configure()
    {
        Get("me");
        Summary(summary =>
        {
            summary.Summary = "Gets the authenticated user's planner account, creating it on first access.";
            summary.Description = "Every authenticated caller has a planner account: this endpoint creates the "
                + "Account and Profile on first access if they do not exist yet. displayName is the user-confirmed "
                + "name or null; suggestedDisplayName is a private, editable suggestion that is never public "
                + "identity. Tacticus integration values are never returned in full — only a masked preview and "
                + "whether onboarding is complete.";
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

        var isNewAccount = account?.Profile is null;
        if (isNewAccount)
        {
            account = await ProvisionAccountAsync(db, state.Issuer, state.Subject, User, ct);
        }

        account!.LastSeenAt = Resolve<TimeProvider>().GetUtcNow();
        await db.SaveChangesAsync(ct);

        var profile = account.Profile!;
        var hasName = profile.DisplayName != NoDisplayName;
        var tacticusApiKey = profile.TacticusIntegration?.TacticusApiKey;
        var analyticsId = Resolve<AnalyticsIdentityDeriver>()
            .Derive(account.Id, AnalyticsIdentityDeriver.PostHogDestination);

        if (isNewAccount)
        {
            ProductAnalyticsReporter.TryReport(
                Resolve<ILogger<GetCurrentUserEndpoint>>(),
                "account_registered",
                () => Resolve<IProductAnalytics>().AccountRegistered(analyticsId)
            );
        }

        await Send.OkAsync(new CurrentUserResponse(
            account.Id.Value,
            hasName ? profile.DisplayName : null,
            hasName ? null : GetDisplayNameSuggestion(User),
            tacticusApiKey is not null,
            SecretMasker.Mask(tacticusApiKey),
            SecretMasker.Mask(profile.TacticusUserId?.Value),
            analyticsId
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
            Type = ProjectType.Default,
        };

        var account = new Account
        {
            Id = AccountId.From(Guid.CreateVersion7()),
            Issuer = issuer,
            Subject = subject,
            Profile = new Profile
            {
                Id = profileId,
                DisplayName = NoDisplayName,
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
        // IgnoreQueryFilters: this runs during first-access provisioning, before CurrentUserPreProcessor has
        // a profile to feed the global query filter — without this, the Profile/TacticusIntegration
        // navigations would look empty even right after they're created below.
        return db.Accounts
            .IgnoreQueryFilters()
            .Include(account => account.Profile)
            .ThenInclude(profile => profile!.TacticusIntegration)
            .FirstOrDefaultAsync(account => account.Id == accountId, ct);
    }

    /// <summary>A private suggestion read live from the token — provider claims are never public identity.</summary>
    private static string? GetDisplayNameSuggestion(ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue("name") ?? user.FindFirstValue("preferred_username");
        return string.IsNullOrWhiteSpace(claim) ? null : claim.Trim();
    }
}

public sealed record CurrentUserResponse(
    Guid ApplicationUserId,
    string? DisplayName,
    string? SuggestedDisplayName,
    bool HasCompletedOnboarding,
    string? TacticusApiKeyMasked,
    string? TacticusUserIdMasked,
    string AnalyticsId
);
