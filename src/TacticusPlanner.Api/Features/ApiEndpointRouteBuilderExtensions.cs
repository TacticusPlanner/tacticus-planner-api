namespace TacticusPlanner.Api.Features;

public static class ApiEndpointRouteBuilderExtensions
{
    public static void MapApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        _ = endpoints
            .MapGroup("/api/v1")
            .RequireAuthorization();
    }
}
