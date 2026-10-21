using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Auth;

/// <summary>
/// Registered once as a global pre-processor (see Program.cs) so every endpoint's identity resolution —
/// previously copy-pasted <c>iss</c>/<c>sub</c> claim extraction plus a profile lookup — happens
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
                await ApiProblems.WriteAsync(httpContext, StatusCodes.Status401Unauthorized);
            }

            return;
        }

        var state = httpContext.ProcessorState<CurrentUserState>();
        state.Issuer = issuer;
        state.Subject = subject;

        var db = httpContext.Resolve<PlannerDbContext>();
        // IgnoreQueryFilters: the profile is not known yet — that's what this query is resolving — so the
        // global profile query filter (which would hide every profile) must not apply here.
        var profileId = await db.Profiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(profile => profile.Issuer == issuer && profile.Subject == subject)
            .Select(profile => (ProfileId?)profile.Id)
            .FirstOrDefaultAsync(ct);

        if (profileId is { } id)
        {
            state.ProfileId = id;
            // Feeds PlannerDbContext's global query filters for the rest of this request — see
            // HttpContextCurrentProfileProvider.
            httpContext.Items[HttpContextCurrentProfileProvider.ProfileIdItemKey] = id;
        }
    }
}
