using TacticusPlanner.Api.Features.CurrentUser;

namespace TacticusPlanner.Api.Features;

public static class ApiEndpointRouteBuilderExtensions
{
    public static void MapApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints
            .MapGroup("/api/v1")
            .RequireAuthorization(AuthorizationPolicies.AccessAsUser);

        api.MapCurrentUserEndpoints();
    }
}
