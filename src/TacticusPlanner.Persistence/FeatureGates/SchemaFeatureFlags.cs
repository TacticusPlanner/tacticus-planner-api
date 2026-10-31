namespace TacticusPlanner.Persistence.FeatureGates;

/// <summary>
/// Feature flags that gate a schema change: their <see cref="FeatureGatedMigrationAttribute"/> migrations are
/// skipped, and the columns those migrations add are left out of the model, wherever the flag is off. The API's
/// <c>FeatureFlags</c> re-exports these names.
/// </summary>
public static class SchemaFeatureFlags
{
    /// <summary>Manual per-battle progress (medals, Lightning Victory, milestones).</summary>
    public const string ManualBattles = "ManualBattles";
}
