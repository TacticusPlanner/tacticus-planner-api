using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Auth;

/// <summary>
/// Resolves <see cref="PlannerDbContext"/>'s global query filter profile from the current request's
/// <see cref="HttpContext.Items"/>, where <see cref="CurrentUserPreProcessor"/> stashes it once resolved.
/// Registered as a singleton (see <see cref="ICurrentProfileProvider"/> for why) — safe because
/// <see cref="IHttpContextAccessor"/> itself resolves the ambient request via <c>AsyncLocal</c>, so each
/// call here still reads the current request's own state rather than a stale/shared one.
/// </summary>
public sealed class HttpContextCurrentProfileProvider(IHttpContextAccessor accessor) : ICurrentProfileProvider
{
    /// <summary>Key <see cref="CurrentUserPreProcessor"/> writes the resolved <see cref="ProfileId"/> under.</summary>
    public static readonly object ProfileIdItemKey = new();

    public ProfileId? ProfileId =>
        accessor.HttpContext?.Items.TryGetValue(ProfileIdItemKey, out var value) == true
            ? value as ProfileId?
            : null;
}
