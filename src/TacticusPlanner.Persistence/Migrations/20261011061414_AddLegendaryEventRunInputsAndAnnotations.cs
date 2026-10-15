using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class AddLegendaryEventRunInputsAndAnnotations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "legendary_event_objective_annotations",
            columns: table => new
            {
                plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                lane_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                battle_index = table.Column<int>(type: "integer", nullable: false),
                objective_id = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_legendary_event_objective_annotations", x => new { x.plan_id, x.lane_id, x.battle_index, x.objective_id });
                table.CheckConstraint("ck_legendary_event_objective_annotations_battle_index", "battle_index >= 0");
                table.CheckConstraint("ck_legendary_event_objective_annotations_lane_id", "lane_id IN ('alpha', 'beta', 'gamma')");
                table.CheckConstraint("ck_legendary_event_objective_annotations_objective_id", "objective_id BETWEEN 0 AND 5");
                table.CheckConstraint("ck_legendary_event_objective_annotations_status", "status IN ('maybeClear', 'stopHere')");
                table.ForeignKey(
                    name: "fk_legendary_event_objective_annotations_legendary_event_plans",
                    column: x => x.plan_id,
                    principalTable: "legendary_event_plans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "legendary_event_run_progress",
            columns: table => new
            {
                plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                run = table.Column<int>(type: "integer", nullable: false),
                regular_missions = table.Column<int>(type: "integer", nullable: false),
                premium_missions = table.Column<int>(type: "integer", nullable: false),
                bundle_purchased = table.Column<bool>(type: "boolean", nullable: false),
                close_shards = table.Column<int>(type: "integer", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_legendary_event_run_progress", x => new { x.plan_id, x.run });
                table.CheckConstraint("ck_legendary_event_run_progress_close_shards", "close_shards >= 0");
                table.CheckConstraint("ck_legendary_event_run_progress_premium_missions", "premium_missions >= 0");
                table.CheckConstraint("ck_legendary_event_run_progress_regular_missions", "regular_missions >= 0");
                table.CheckConstraint("ck_legendary_event_run_progress_run", "run BETWEEN 1 AND 3");
                table.ForeignKey(
                    name: "fk_legendary_event_run_progress_legendary_event_plans_plan_id",
                    column: x => x.plan_id,
                    principalTable: "legendary_event_plans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "legendary_event_objective_annotations");

        migrationBuilder.DropTable(
            name: "legendary_event_run_progress");
    }
}
