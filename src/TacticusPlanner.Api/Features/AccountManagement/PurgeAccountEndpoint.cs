using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.AccountManagement;

public sealed class PurgeAccountEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("me");
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes the authenticated user's planner account and all related data.";
            summary.Description = "Deletes the profile and every row it owns for the caller. "
                + "This cannot be undone. Also makes a best-effort attempt to delete the corresponding Microsoft "
                + "Entra External ID identity; see IExternalIdentityDeleter for the current limitation. "
                + "Idempotent: repeating the request after the account is gone still succeeds.";
            summary.Response(StatusCodes.Status204NoContent, "The account and all related data were deleted.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden, "The authenticated user cannot access the API.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();

        if (state.ProfileId is { } profileId)
        {
            // Profile-owned rows cascade with the profile and guild references are set null, so one statement
            // removes the whole account.
            await Resolve<PlannerDbContext>().Profiles
                .Where(entity => entity.Id == profileId)
                .ExecuteDeleteAsync(ct);
        }

        // Best-effort only: application-side deletion above is authoritative and already complete regardless
        // of whether the external identity could also be removed.
        await Resolve<IExternalIdentityDeleter>().TryDeleteAsync(state.Issuer, state.Subject, ct);

        await Send.NoContentAsync(ct);
    }
}
