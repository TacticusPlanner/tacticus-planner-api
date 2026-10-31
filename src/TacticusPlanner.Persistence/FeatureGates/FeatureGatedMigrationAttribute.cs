namespace TacticusPlanner.Persistence.FeatureGates;

/// <summary>
/// Applies the migration only where <see cref="Flag"/> is on when the API starts (see
/// <see cref="SchemaFeatureGate"/>). Elsewhere the migration stays pending, and it runs on the first start after the
/// flag is turned on. Gate additive changes only (a new table or a new nullable column), and leave what they add
/// out of the model while the flag is off (see <c>PlannerDbContext.ApplySchemaFeatureGates</c>).
/// </summary>
/// <remarks>
/// When the feature ships, delete the attribute, the model exclusion, and the flag in one change.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FeatureGatedMigrationAttribute(string flag) : Attribute
{
    public string Flag { get; } = flag;
}
