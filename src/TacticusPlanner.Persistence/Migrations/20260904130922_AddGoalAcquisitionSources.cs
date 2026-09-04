using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class AddGoalAcquisitionSources : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The `config` jsonb column keeps PascalCase element names (EFCore.NamingConventions only
        // rewrites relational column names, not ToJson element names). This migration replaces the
        // removed `AscensionFarming` object and the Unlock shard use of `FarmingLocationIds` with a
        // `AcquisitionSources` array of `{ Kind, Ids }` entries. One-way (see Down).
        migrationBuilder.Sql("""
            -- Unlock / Ascension goals: express the shard-source choice as AcquisitionSources.
            UPDATE goals
            SET config = jsonb_set(
                config #- '{AscensionFarming}',
                '{AcquisitionSources}',
                jsonb_build_array(
                    jsonb_build_object(
                        'Kind', 'Campaign',
                        'Ids', COALESCE(
                            CASE
                                WHEN jsonb_typeof(config -> 'AscensionFarming') = 'object'
                                    THEN COALESCE(config -> 'AscensionFarming' -> 'ShardBattleIds', '[]'::jsonb)
                                       || COALESCE(config -> 'AscensionFarming' -> 'MythicShardBattleIds', '[]'::jsonb)
                                WHEN goal_type = 'Unlock' AND jsonb_typeof(config -> 'FarmingLocationIds') = 'array'
                                    THEN config -> 'FarmingLocationIds'
                                ELSE '[]'::jsonb
                            END,
                            '[]'::jsonb)))
                || CASE
                    WHEN (config -> 'AscensionFarming' ->> 'Source') IN ('1', '2')
                        THEN jsonb_build_array(jsonb_build_object('Kind', 'Onslaught', 'Ids', '[]'::jsonb))
                    ELSE '[]'::jsonb
                END)
            WHERE goal_type IN ('Unlock', 'Ascension');

            -- Unlock goals: the shard-node override moved into AcquisitionSources; clear the old field.
            UPDATE goals
            SET config = jsonb_set(config, '{FarmingLocationIds}', 'null'::jsonb)
            WHERE goal_type = 'Unlock';

            -- Every remaining goal: drop the removed AscensionFarming key.
            UPDATE goals
            SET config = config - 'AscensionFarming'
            WHERE config ? 'AscensionFarming';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "AddGoalAcquisitionSources rewrites goal config jsonb and drops the AscensionFarming model. "
            + "Restore a database backup instead of attempting an automatic downgrade.");
}
