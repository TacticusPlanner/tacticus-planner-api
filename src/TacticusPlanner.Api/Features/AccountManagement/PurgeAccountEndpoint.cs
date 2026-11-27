using System.Security.Claims;
using FastEndpoints;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.AccountManagement;

public sealed class PurgeAccountRequest
{
    /// <summary>
    /// True also deletes the Microsoft Entra External ID user the caller signs in with. False (the default)
    /// deletes only planner data, so the same sign-in starts a fresh planner account.
    /// </summary>
    [QueryParam]
    public bool? DeleteSignInAccount { get; init; }
}

public sealed class PurgeAccountEndpoint : Endpoint<PurgeAccountRequest>
{
    /// <summary>Response header on the 204: <c>true</c> only when the Entra sign-in account was actually
    /// deleted, <c>false</c> when it was not asked for or the deletion did not complete during the request (it is
    /// then retried in the background by <see cref="ExternalIdentityDeletionJob"/>).</summary>
    public const string SignInAccountDeletedHeader = "X-Sign-In-Account-Deleted";

    public override void Configure()
    {
        Delete("account");
        Summary(summary =>
        {
            summary.Summary = "Permanently deletes the authenticated user's planner account and all related data.";
            summary.Description = "Deletes the profile and every row it owns for the caller. "
                + "This cannot be undone. With deleteSignInAccount=true it also makes a best-effort attempt to "
                + "delete the caller's Microsoft Entra External ID user (see IExternalIdentityDeleter). "
                + "The " + SignInAccountDeletedHeader + " response header (true|false) reports whether the sign-in "
                + "account was actually deleted during the request; when it was not, the deletion is retried in the "
                + "background. "
                + "Idempotent: repeating the request after the account is gone still succeeds.";
            summary.Response(StatusCodes.Status204NoContent, "The account and all related data were deleted.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status403Forbidden, "The authenticated user cannot access the API.");
        });
    }

    public override async Task HandleAsync(PurgeAccountRequest req, CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();

        if (state.ProfileId is { } profileId)
        {
            // Profile-owned rows cascade with the profile and guild references are set null by the database;
            // the guild-visible copies derived from the profile (name, last-active) are cleared alongside, in
            // one transaction, so guildmates stop seeing them without waiting for the next guild sync.
            var db = Resolve<PlannerDbContext>();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.GuildMembers
                    .Where(entity => entity.ProfileId == profileId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(entity => entity.ProfileId, (ProfileId?)null)
                        .SetProperty(entity => entity.LinkedPlayerName, (string?)null)
                        .SetProperty(entity => entity.LastActiveInPlannerOn, (DateTimeOffset?)null)
                        .SetProperty(entity => entity.Revision, entity => entity.Revision + 1), ct);
                await db.Profiles
                    .Where(entity => entity.Id == profileId)
                    .ExecuteDeleteAsync(ct);
                await transaction.CommitAsync(ct);
            });
        }

        var signInAccountDeleted = false;
        if (req.DeleteSignInAccount == true)
        {
            // Best-effort only: application-side deletion above is authoritative and already complete regardless
            // of whether the external identity could also be removed.
            var deleter = Resolve<IExternalIdentityDeleter>();
            var objectId = User.FindFirstValue("oid");
            signInAccountDeleted = await deleter.TryDeleteAsync(objectId, ct);

            // A token without a usable oid can never succeed, and without a Graph credential nothing would.
            if (!signInAccountDeleted && deleter.IsConfigured && Guid.TryParse(objectId, out _))
            {
                Resolve<IBackgroundJobClient>().Enqueue<ExternalIdentityDeletionJob>(
                    job => job.RunAsync(objectId, CancellationToken.None));
            }
        }

        HttpContext.Response.Headers[SignInAccountDeletedHeader] = signInAccountDeleted ? "true" : "false";
        await Send.NoContentAsync(ct);
    }
}
