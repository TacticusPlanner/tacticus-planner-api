using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace TacticusPlanner.Persistence.FeatureGates;

/// <summary>
/// Which schema feature flags were off when the process started. Read once, because the model and the applied
/// migrations cannot change while the process runs: a flag turned on later takes effect for the schema on the next
/// restart.
/// </summary>
public sealed class SchemaFeatureGate
{
    /// <summary>Every gated migration applies and every gated column is mapped. Used wherever no gate is
    /// configured, such as tests and design-time tools.</summary>
    public static SchemaFeatureGate AllEnabled { get; } = new([]);

    /// <summary>Every flag named by a <see cref="FeatureGatedMigrationAttribute"/>, in ordinal order.</summary>
    public static IReadOnlyList<string> GatedFlags { get; } = typeof(PlannerDbContext).Assembly
        .GetTypes()
        .Select(type => type.GetCustomAttribute<FeatureGatedMigrationAttribute>()?.Flag)
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.Ordinal)
        .ToArray();

    public SchemaFeatureGate(IEnumerable<string> disabledFlags)
    {
        DisabledFlags = disabledFlags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Gated flags that are off, in ordinal order.</summary>
    public IReadOnlyList<string> DisabledFlags { get; }

    /// <summary>
    /// False only for a gated flag that was off at startup. Always true for design-time tools (<c>dotnet ef</c>), so
    /// generated migrations and the model snapshot always include every gated change.
    /// </summary>
    public bool IsEnabled(string flag) =>
        EF.IsDesignTime || !DisabledFlags.Contains(flag, StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads each of the <see cref="GatedFlags"/> once.</summary>
    public static async Task<SchemaFeatureGate> EvaluateAsync(Func<string, Task<bool>> isFlagEnabled)
    {
        var disabled = new List<string>();
        foreach (var flag in GatedFlags)
        {
            if (!await isFlagEnabled(flag))
            {
                disabled.Add(flag);
            }
        }

        return new SchemaFeatureGate(disabled);
    }

    internal string CacheKey => string.Join(',', DisabledFlags);
}
