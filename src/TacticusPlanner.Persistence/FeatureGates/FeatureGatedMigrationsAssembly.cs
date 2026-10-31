using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

namespace TacticusPlanner.Persistence.FeatureGates;

#pragma warning disable EF1001 // MigrationsAssembly is EF's own implementation; this only narrows its migration list.

/// <summary>
/// Hides each <see cref="FeatureGatedMigrationAttribute"/> migration whose flag is off, so <c>Migrate</c> neither
/// applies it nor lists it as pending. It is not recorded in the history table, so it applies once the flag is on.
/// </summary>
public sealed class FeatureGatedMigrationsAssembly(
    ICurrentDbContext currentContext,
    IDbContextOptions options,
    IMigrationsIdGenerator idGenerator,
    IDiagnosticsLogger<DbLoggerCategory.Migrations> logger
) : MigrationsAssembly(currentContext, options, idGenerator, logger)
{
    private readonly SchemaFeatureGate gate = SchemaFeatureGateOptionsExtension.GateFrom(options);
    private IReadOnlyDictionary<string, TypeInfo>? migrations;

    public override IReadOnlyDictionary<string, TypeInfo> Migrations => migrations ??= base.Migrations
        .Where(migration => migration.Value.GetCustomAttribute<FeatureGatedMigrationAttribute>() is not { } gated
            || gate.IsEnabled(gated.Flag))
        .ToDictionary(migration => migration.Key, migration => migration.Value);
}

#pragma warning restore EF1001
