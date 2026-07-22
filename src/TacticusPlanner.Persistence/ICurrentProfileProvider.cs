using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Persistence;

/// <summary>
/// Resolves the profile the current request is scoped to, for <see cref="PlannerDbContext"/>'s global
/// query filters (see <c>PlannerDbContext.ApplyProfileQueryFilters</c>). Registered as a singleton — the
/// same as <c>IColumnEncryptionService</c> — because <see cref="PlannerDbContext"/> is pooled (Aspire's
/// <c>AddNpgsqlDbContext</c>) and cannot depend on a scoped service; implementations must instead resolve
/// the current request's identity per call (e.g. via <c>IHttpContextAccessor</c>).
/// </summary>
public interface ICurrentProfileProvider
{
    /// <summary>Null when there is no authenticated caller, or the caller has not been provisioned a
    /// profile yet. A null value makes every profile-owned query filter evaluate to "no rows" rather than
    /// accidentally matching an unscoped/null-ProfileId row.</summary>
    ProfileId? ProfileId { get; }
}
