using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Analytics;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.CurrentUser;

public sealed class GetCurrentUserEndpoint : EndpointWithoutRequest<CurrentUserResponse>
{
    /// <summary>Stored until the user sets a name; a chosen name is always 1-80 characters, so this can never
    /// collide with one. Provider claims are never persisted as the name.</summary>
    public const string NoDisplayName = "";

    private static readonly TimeSpan LastSeenWriteInterval = TimeSpan.FromHours(1);

    public override void Configure()
    {
        Get("account");
        Summary(summary =>
        {
            summary.Summary = "Gets the authenticated user's planner account, creating it on first access.";
            summary.Description = "Every authenticated caller has a planner account: this endpoint creates the "
                + "profile on first access if they do not exist yet. displayName is the user-confirmed "
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

        var profile = state.ProfileId is { } profileId
            ? await FindProfileAsync(db, profileId, ct)
            : null;

        var isNewAccount = profile is null;
        if (isNewAccount)
        {
            profile = await ProvisionProfileAsync(db, state.Issuer, state.Subject, ct);
        }

        // last_seen_at only feeds the "last active in Planner" display, so hourly precision is enough and
        // the common repeat call writes nothing.
        var now = Resolve<TimeProvider>().GetUtcNow();
        if (profile!.LastSeenAt is not { } lastSeenAt || now - lastSeenAt >= LastSeenWriteInterval)
        {
            profile.LastSeenAt = now;
            await db.SaveChangesAsync(ct);
        }

        var hasName = profile.DisplayName != NoDisplayName;
        var tacticusApiKey = profile.TacticusIntegration?.TacticusApiKey;
        var analyticsId = Resolve<AnalyticsIdentityDeriver>()
            .Derive(profile.Id, AnalyticsIdentityDeriver.PostHogDestination);

        if (isNewAccount)
        {
            ProductAnalyticsReporter.TryReport(
                Resolve<ILogger<GetCurrentUserEndpoint>>(),
                "account_registered",
                () => Resolve<IProductAnalytics>().AccountRegistered(analyticsId)
            );
        }

        await Send.OkAsync(new CurrentUserResponse(
            profile.Id.Value,
            hasName ? profile.DisplayName : null,
            hasName ? null : GetDisplayNameSuggestion(User),
            tacticusApiKey is not null,
            SecretMasker.Mask(tacticusApiKey),
            SecretMasker.Mask(profile.TacticusUserId?.Value),
            analyticsId
        ), ct);
    }

    private static async Task<Profile> ProvisionProfileAsync(
        PlannerDbContext db,
        string issuer,
        string subject,
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

        var profile = new Profile
        {
            Id = profileId,
            Issuer = issuer,
            Subject = subject,
            DisplayName = NoDisplayName,
        };

        db.Profiles.Add(profile);
        db.Projects.Add(defaultProject);

        await db.SaveChangesAsync(ct);

        return profile;
    }

    private static Task<Profile?> FindProfileAsync(PlannerDbContext db, ProfileId profileId, CancellationToken ct)
    {
        // IgnoreQueryFilters: harmless once CurrentUserPreProcessor has fed the filter, and keeps the
        // TacticusIntegration navigation visible regardless of filter state.
        return db.Profiles
            .IgnoreQueryFilters()
            .Include(profile => profile.TacticusIntegration)
            .FirstOrDefaultAsync(profile => profile.Id == profileId, ct);
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
