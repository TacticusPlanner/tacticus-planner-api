using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace TacticusPlanner.Persistence.FeatureGates;

/// <summary>Builds a separate model for each set of disabled schema flags, because each leaves different columns
/// out (see <c>PlannerDbContext.ApplySchemaFeatureGates</c>).</summary>
public sealed class SchemaFeatureModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (
        context.GetType(),
        SchemaFeatureGateOptionsExtension.GateFrom(context.GetService<IDbContextOptions>()).CacheKey,
        designTime);
}
