using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class RedesignGoalsProjectsManagement : Migration
{
    private static readonly string[] ProjectSlotColumns = ["project_id", "entity_type", "entity_id", "goal_type"];
    private static readonly string[] LegacyGoalSlotColumns = ["profile_id", "entity_type", "entity_id", "goal_type"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_goals_one_active_or_paused_per_entity_and_type",
            table: "goals");

        migrationBuilder.AddColumn<string>(
            name: "entity_id",
            table: "project_goals",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "entity_type",
            table: "project_goals",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "goal_type",
            table: "project_goals",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<bool>(
            name: "occupies_in_flight_slot",
            table: "project_goals",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // Equipment goals are intentionally removed from the planner model. Player equipment and
        // catalog data remain untouched; cascading deletes remove only their project memberships.
        migrationBuilder.Sql("""
            DELETE FROM goals
            WHERE entity_type = 'Item' OR goal_type = 'UpgradeItem';
            """);

        migrationBuilder.Sql("""
            UPDATE project_goals AS membership
            SET entity_type = goal.entity_type,
                entity_id = goal.entity_id,
                goal_type = goal.goal_type,
                occupies_in_flight_slot = goal.status IN ('Active', 'Paused')
            FROM goals AS goal
            WHERE membership.goal_id = goal.id;
            """);

        migrationBuilder.CreateIndex(
            name: "ix_project_goals_one_in_flight_slot",
            table: "project_goals",
            columns: ProjectSlotColumns,
            unique: true,
            filter: "occupies_in_flight_slot = TRUE");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_project_goals_one_in_flight_slot",
            table: "project_goals");

        migrationBuilder.DropColumn(
            name: "entity_id",
            table: "project_goals");

        migrationBuilder.DropColumn(
            name: "entity_type",
            table: "project_goals");

        migrationBuilder.DropColumn(
            name: "goal_type",
            table: "project_goals");

        migrationBuilder.DropColumn(
            name: "occupies_in_flight_slot",
            table: "project_goals");

        migrationBuilder.CreateIndex(
            name: "ix_goals_one_active_or_paused_per_entity_and_type",
            table: "goals",
            columns: LegacyGoalSlotColumns,
            unique: true,
            filter: "status IN ('Active', 'Paused')");
    }
}
