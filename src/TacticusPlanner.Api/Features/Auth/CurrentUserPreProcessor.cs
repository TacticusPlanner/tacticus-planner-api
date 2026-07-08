using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Users;

namespace TacticusPlanner.Api.Features.Auth;

/// <summary>
/// Registered once as a global pre-processor (see Program.cs) so every endpoint's identity resolution —
/// previously copy-pasted <c>iss</c>/<c>sub</c> claim extraction plus an Account/Profile lookup — happens
/// exactly once per request. Populates <see cref="CurrentUserState"/> for <c>ProcessorState&lt;T&gt;()</c>
/// to read.
///
/// Runs for every endpoint, including anonymous ones (e.g. the public game-catalog routes): unauthenticated
/// requests are left alone (no claims to read, no query to run) so anonymous endpoints stay anonymous.
/// </summary>
public sealed class CurrentUserPreProcessor : IGlobalPreProcessor
{
    public async Task PreProcessAsync(IPreProcessorContext context, CancellationToken ct)
    {
        var httpContext = context.HttpContext;
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var issuer = httpContext.User.FindFirstValue("iss");
        var subject = httpContext.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            // Defense in depth: the AccessAsUser authorization policy already requires a "sub" claim for
            // every non-anonymous endpoint, so this should be unreachable in practice — kept so a caller
            // still gets a clean 401 rather than a null-reference surprise if that ever changes.
            if (!httpContext.ResponseStarted())
            {
                await httpContext.Response.SendUnauthorizedAsync(ct);
            }

            return;
        }

        var state = httpContext.ProcessorState<CurrentUserState>();
        state.Issuer = issuer;
        state.Subject = subject;

        var db = httpContext.Resolve<PlannerDbContext>();
        var ids = await db.Accounts
            .AsNoTracking()
            .Where(account => account.Issuer == issuer && account.Subject == subject)
            .Select(account => new
            {
                account.Id,
                ProfileId = account.Profile != null ? (ProfileId?)account.Profile.Id : null,
            })
            .SingleOrDefaultAsync(ct);

        if (ids is not null)
        {
            state.AccountId = ids.Id;
            state.ProfileId = ids.ProfileId;
        }
    }
}
