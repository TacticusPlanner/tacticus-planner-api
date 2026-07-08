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
            summary.Description = "Deletes the Account, Profile, and Tacticus integration rows for the caller. "
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

        if (state.AccountId is { } accountId)
        {
            var db = Resolve<PlannerDbContext>();
            var account = await db.Accounts.SingleOrDefaultAsync(entity => entity.Id == accountId, ct);

            if (account is not null)
            {
                // Cascade delete (configured in AccountConfiguration/TacticusIntegrationConfiguration) removes
                // the Profile and TacticusIntegration rows along with the Account.
                db.Accounts.Remove(account);
                await db.SaveChangesAsync(ct);
            }
        }

        // Best-effort only: application-side deletion above is authoritative and already complete regardless
        // of whether the external identity could also be removed.
        await Resolve<IExternalIdentityDeleter>().TryDeleteAsync(state.Issuer, state.Subject, ct);

        await Send.NoContentAsync(ct);
    }
}
