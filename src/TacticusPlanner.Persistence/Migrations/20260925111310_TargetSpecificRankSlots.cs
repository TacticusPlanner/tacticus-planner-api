using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class TargetSpecificRankSlots : Migration
{
    private static readonly string[] ProjectSlotColumns = ["project_id", "entity_type", "entity_id", "goal_type"];
    private static readonly string[] RankTargetColumns = ["project_id", "entity_type", "entity_id", "rank_target_key"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_project_goals_one_in_flight_slot",
            table: "project_goals");

        migrationBuilder.AddColumn<string>(
            name: "rank_target_key",
            table: "project_goals",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        // Backfill the normalized end target for existing Rank memberships — the same rule as the
        // C# RankTargetKey.From: below Adamantine1 point-five means all 3 slots (else capped at 3),
        // at Adamantine1+ the applied count is used as-is. The old index allowed one in-flight Rank
        // goal per project/unit, so the new unique index cannot be violated by existing rows.
        migrationBuilder.Sql("""
            UPDATE project_goals AS membership
            SET rank_target_key =
                (goal.config -> 'Rank' ->> 'End') || ':' ||
                CASE
                    WHEN (goal.config -> 'Rank' ->> 'End')::int >= 18 THEN
                        GREATEST(COALESCE((goal.config -> 'Rank' ->> 'EndAppliedUpgrades')::int, 0), 0)
                    WHEN COALESCE((goal.config -> 'Rank' ->> 'EndPointFive')::boolean, FALSE) THEN 3
                    ELSE LEAST(GREATEST(COALESCE((goal.config -> 'Rank' ->> 'EndAppliedUpgrades')::int, 0), 0), 3)
                END
            FROM goals AS goal
            WHERE membership.goal_id = goal.id
              AND goal.goal_type = 'Rank'
              AND jsonb_typeof(goal.config -> 'Rank') = 'object';
            """);

        migrationBuilder.CreateIndex(
            name: "ix_project_goals_one_in_flight_rank_target",
            table: "project_goals",
            columns: RankTargetColumns,
            unique: true,
            filter: "occupies_in_flight_slot = TRUE AND goal_type = 'Rank'");

        migrationBuilder.CreateIndex(
            name: "ix_project_goals_one_in_flight_slot",
            table: "project_goals",
            columns: ProjectSlotColumns,
            unique: true,
            filter: "occupies_in_flight_slot = TRUE AND goal_type <> 'Rank'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restoring the old one-Rank-per-unit index fails while a project holds several in-flight Rank
        // milestones; that needs a data review, not automatic deletion (see the change's design).
        migrationBuilder.DropIndex(
            name: "ix_project_goals_one_in_flight_rank_target",
            table: "project_goals");

        migrationBuilder.DropIndex(
            name: "ix_project_goals_one_in_flight_slot",
            table: "project_goals");

        migrationBuilder.DropColumn(
            name: "rank_target_key",
            table: "project_goals");

        migrationBuilder.CreateIndex(
            name: "ix_project_goals_one_in_flight_slot",
            table: "project_goals",
            columns: ProjectSlotColumns,
            unique: true,
            filter: "occupies_in_flight_slot = TRUE");
    }
}
