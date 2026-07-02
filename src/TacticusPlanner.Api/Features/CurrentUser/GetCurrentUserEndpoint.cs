using System.Security.Claims;
using FastEndpoints;

namespace TacticusPlanner.Api.Features.CurrentUser;

public sealed class GetCurrentUserEndpoint : EndpointWithoutRequest<CurrentUserResponse>
{
    public override void Configure()
    {
        Get("me");
        Summary(summary =>
        {
            summary.Summary = "Gets the authenticated user.";
            summary.Response<CurrentUserResponse>(
                StatusCodes.Status200OK,
                "The authenticated user's token-derived profile."
            );
            summary.Response(StatusCodes.Status401Unauthorized, "The request is not authenticated.");
            summary.Response(StatusCodes.Status403Forbidden, "The authenticated user cannot access the API.");
        });
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        var userId = User.FindFirstValue("oid")
            ?? User.FindFirstValue("sub")!;
        var displayName = User.FindFirstValue("name");
        var email = User.FindFirstValue("email")
            ?? User.FindFirstValue("preferred_username")
            ?? User.FindFirstValue("emails");

        return Send.OkAsync(new CurrentUserResponse(userId, displayName, email), ct);
    }
}

public sealed record CurrentUserResponse(
    string UserId,
    string? DisplayName,
    string? Email
);
