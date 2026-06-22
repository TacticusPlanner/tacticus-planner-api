using System.Security.Claims;

namespace TacticusPlanner.Api.Features.CurrentUser;

public static class CurrentUserEndpoints
{
    public static void MapCurrentUserEndpoints(this RouteGroupBuilder api)
    {
        _ = api
            .MapGet("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .WithSummary("Gets the authenticated user")
            .Produces<CurrentUserResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }

    private static IResult GetCurrentUser(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue("oid")
            ?? principal.FindFirstValue("sub")!;
        var displayName = principal.FindFirstValue("name");
        var email = principal.FindFirstValue("email")
            ?? principal.FindFirstValue("preferred_username")
            ?? principal.FindFirstValue("emails");

        return Results.Ok(new CurrentUserResponse(userId, displayName, email));
    }
}

public sealed record CurrentUserResponse(
    string UserId,
    string? DisplayName,
    string? Email
);
