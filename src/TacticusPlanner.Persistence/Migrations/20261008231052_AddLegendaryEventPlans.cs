using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class AddLegendaryEventPlans : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "legendary_event_plans",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false),
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                catalog_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                show_paid_options = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_legendary_event_plans", x => x.id);
                table.ForeignKey(
                    name: "fk_legendary_event_plans_profiles_profile_id",
                    column: x => x.profile_id,
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "legendary_event_teams",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                lane_id = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_legendary_event_teams", x => x.id);
                table.CheckConstraint("ck_legendary_event_teams_lane_id", "lane_id IN ('alpha', 'beta', 'gamma')");
                table.CheckConstraint("ck_legendary_event_teams_sort_order", "sort_order >= 0");
                table.ForeignKey(
                    name: "fk_legendary_event_teams_legendary_event_plans_plan_id",
                    column: x => x.plan_id,
                    principalTable: "legendary_event_plans",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "legendary_event_team_members",
            columns: table => new
            {
                team_id = table.Column<Guid>(type: "uuid", nullable: false),
                reserve = table.Column<bool>(type: "boolean", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                unit_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_legendary_event_team_members", x => new { x.team_id, x.reserve, x.position });
                table.CheckConstraint("ck_legendary_event_team_members_position", "(reserve = FALSE AND position BETWEEN 0 AND 4) OR (reserve = TRUE AND position = 0)");
                table.ForeignKey(
                    name: "fk_legendary_event_team_members_legendary_event_teams_team_id",
                    column: x => x.team_id,
                    principalTable: "legendary_event_teams",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "legendary_event_team_objectives",
            columns: table => new
            {
                team_id = table.Column<Guid>(type: "uuid", nullable: false),
                objective_index = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_legendary_event_team_objectives", x => new { x.team_id, x.objective_index });
                table.CheckConstraint("ck_legendary_event_team_objectives_index", "objective_index >= 0");
                table.ForeignKey(
                    name: "fk_legendary_event_team_objectives_legendary_event_teams_team_",
                    column: x => x.team_id,
                    principalTable: "legendary_event_teams",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "legendary_event_team_run_depths",
            columns: table => new
            {
                team_id = table.Column<Guid>(type: "uuid", nullable: false),
                run = table.Column<int>(type: "integer", nullable: false),
                expected_battle_clears = table.Column<int>(type: "integer", nullable: false),
                source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_legendary_event_team_run_depths", x => new { x.team_id, x.run });
                table.CheckConstraint("ck_legendary_event_team_run_depths_depth", "expected_battle_clears >= 1");
                table.CheckConstraint("ck_legendary_event_team_run_depths_run", "run BETWEEN 1 AND 3");
                table.CheckConstraint("ck_legendary_event_team_run_depths_source", "source IN ('Estimate', 'Manual')");
                table.ForeignKey(
                    name: "fk_legendary_event_team_run_depths_legendary_event_teams_team_",
                    column: x => x.team_id,
                    principalTable: "legendary_event_teams",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_legendary_event_plans_profile_id_event_id",
            table: "legendary_event_plans",
            columns: ["profile_id", "event_id"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_legendary_event_team_members_team_id_unit_id",
            table: "legendary_event_team_members",
            columns: ["team_id", "unit_id"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_legendary_event_teams_plan_id_lane_id",
            table: "legendary_event_teams",
            columns: ["plan_id", "lane_id"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "legendary_event_team_members");

        migrationBuilder.DropTable(
            name: "legendary_event_team_objectives");

        migrationBuilder.DropTable(
            name: "legendary_event_team_run_depths");

        migrationBuilder.DropTable(
            name: "legendary_event_teams");

        migrationBuilder.DropTable(
            name: "legendary_event_plans");
    }
}
