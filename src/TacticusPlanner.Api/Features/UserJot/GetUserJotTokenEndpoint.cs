using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.CurrentUser;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.UserJot;

/// <summary>Optional feedback context the client attaches to its identity token. Each value is echoed into the
/// token's <c>traits</c> only when it has the expected shape; anything else is dropped, never rejected, so an
/// older client that sends none keeps working.</summary>
public sealed class GetUserJotTokenRequest
{
    /// <summary>The running app build: a release tag, version or commit SHA (at most 40 characters).</summary>
    [QueryParam]
    public string? AppVersion { get; init; }

    /// <summary>How the app is running: <c>installed</c> (the installed app) or <c>browser</c>.</summary>
    [QueryParam]
    public string? AppMode { get; init; }

    /// <summary>The app's active UI language, such as <c>en</c> or <c>pt-BR</c>.</summary>
    [QueryParam]
    public string? Language { get; init; }
}

public sealed class GetUserJotTokenEndpoint : Endpoint<GetUserJotTokenRequest, UserJotTokenResponse>
{
    public const string FallbackName = "Planner User";

    public override void Configure()
    {
        Get("userjot-token");
        Summary(summary =>
        {
            summary.Summary = "Issues a short-lived signed identity token for the UserJot feedback widget.";
            summary.Description = "The token proves the caller's identity to UserJot's widget SDK (signed "
                + "identity) so feedback and messages cannot be attributed to a spoofed identity from the "
                + "browser. Each call returns a fresh token, valid for at most one hour. Its traits carry non-identifying "
                + "feedback context: whether a Tacticus API key is linked, plus the optional appVersion, appMode and "
                + "language query values when they are well formed.";
            summary.Response<UserJotTokenResponse>(
                StatusCodes.Status200OK,
                "A signed UserJot identity token for the current user."
            );
            summary.Response(StatusCodes.Status401Unauthorized, "The request is not authenticated.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated user has not signed up yet.");
        });
    }

    public override async Task HandleAsync(GetUserJotTokenRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        // Public identity: only a name the user confirmed. Provider/V1/legacy suggestions never reach UserJot.
        var profile = await db.Profiles
            .Where(entity => entity.Id == profileId)
            .Select(entity => new
            {
                entity.DisplayName,
                TacticusApiLinked = entity.TacticusIntegration != null
                    && entity.TacticusIntegration.TacticusApiKey != null,
            })
            .FirstAsync(ct);
        var displayName = profile.DisplayName;
        if (displayName == GetCurrentUserEndpoint.NoDisplayName)
        {
            displayName = FallbackName;
        }

        var traits = UserJotTraits.Build(req.AppVersion, req.AppMode, req.Language, profile.TacticusApiLinked);
        var token = Resolve<UserJotTokenSigner>().CreateToken(profileId.Value, displayName, traits);

        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.OkAsync(new UserJotTokenResponse(token), ct);
    }
}

public sealed record UserJotTokenResponse(string Token);
