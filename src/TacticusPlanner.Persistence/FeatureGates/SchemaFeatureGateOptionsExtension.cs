using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace TacticusPlanner.Persistence.FeatureGates;

/// <summary>Carries the <see cref="SchemaFeatureGate"/> in the context options, where the model, the migrations
/// assembly and the model cache can all read it. Without it, everything is enabled.</summary>
public sealed class SchemaFeatureGateOptionsExtension(SchemaFeatureGate gate) : IDbContextOptionsExtension
{
    public SchemaFeatureGate Gate { get; } = gate;

    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    internal static SchemaFeatureGate GateFrom(IDbContextOptions options) =>
        options.FindExtension<SchemaFeatureGateOptionsExtension>()?.Gate ?? SchemaFeatureGate.AllEnabled;

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        private new SchemaFeatureGateOptionsExtension Extension => (SchemaFeatureGateOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"SchemaFeatureGate(disabled: {Extension.Gate.CacheKey}) ";

        public override int GetServiceProviderHashCode() =>
            StringComparer.Ordinal.GetHashCode(Extension.Gate.CacheKey);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo otherInfo
            && string.Equals(Extension.Gate.CacheKey, otherInfo.Extension.Gate.CacheKey, StringComparison.Ordinal);

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["SchemaFeatureGate:Disabled"] = Extension.Gate.CacheKey;
    }
}

public static class SchemaFeatureGateDbContextOptionsExtensions
{
    /// <summary>
    /// Skips the gated migrations and leaves the gated columns out of the model for each flag
    /// <paramref name="gate"/> reports as off.
    /// </summary>
    public static DbContextOptionsBuilder UseSchemaFeatureGate(this DbContextOptionsBuilder options, SchemaFeatureGate gate)
    {
        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(new SchemaFeatureGateOptionsExtension(gate));
        options.ReplaceService<IMigrationsAssembly, FeatureGatedMigrationsAssembly>();
        options.ReplaceService<IModelCacheKeyFactory, SchemaFeatureModelCacheKeyFactory>();
        if (gate.DisabledFlags.Count > 0)
        {
            // The snapshot always includes every gated column, so a model without one always differs from it.
            // Pending-change detection still runs wherever every gated flag is on, such as CI and local runs.
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        }

        return options;
    }
}
