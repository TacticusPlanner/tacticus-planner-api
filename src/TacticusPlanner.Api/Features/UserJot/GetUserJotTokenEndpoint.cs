using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.CurrentUser;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.UserJot;

public sealed class GetUserJotTokenEndpoint : EndpointWithoutRequest<UserJotTokenResponse>
{
    public const string FallbackName = "Planner User";

    public override void Configure()
    {
        Get("me/userjot-token");
        Summary(summary =>
        {
            summary.Summary = "Issues a short-lived signed identity token for the UserJot feedback widget.";
            summary.Description = "The token proves the caller's identity to UserJot's widget SDK (signed "
                + "identity) so feedback and messages cannot be attributed to a spoofed identity from the "
                + "browser. Each call returns a fresh token, valid for at most one hour.";
            summary.Response<UserJotTokenResponse>(
                StatusCodes.Status200OK,
                "A signed UserJot identity token for the current user."
            );
            summary.Response(StatusCodes.Status401Unauthorized, "The request is not authenticated.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated user has not signed up yet.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.AccountId is not { } accountId || state.ProfileId is not { } profileId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        // Public identity: only a name the user confirmed. Provider/V1/legacy suggestions never reach UserJot.
        var displayName = await db.Profiles
            .Where(profile => profile.Id == profileId)
            .Select(profile => profile.DisplayName)
            .FirstAsync(ct);
        if (displayName == GetCurrentUserEndpoint.NoDisplayName)
        {
            displayName = FallbackName;
        }

        var token = Resolve<UserJotTokenSigner>().CreateToken(accountId.Value, displayName);

        HttpContext.Response.Headers.CacheControl = "no-store";
        await Send.OkAsync(new UserJotTokenResponse(token), ct);
    }
}

public sealed record UserJotTokenResponse(string Token);
