using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class AddLegendaryEventCompletedMissions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<List<int>>(
            name: "completed_premium_missions",
            table: "legendary_event_run_progress",
            type: "integer[]",
            nullable: false,
            defaultValueSql: "'{}'");

        migrationBuilder.AddColumn<List<int>>(
            name: "completed_regular_missions",
            table: "legendary_event_run_progress",
            type: "integer[]",
            nullable: false,
            defaultValueSql: "'{}'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "completed_premium_missions",
            table: "legendary_event_run_progress");

        migrationBuilder.DropColumn(
            name: "completed_regular_missions",
            table: "legendary_event_run_progress");
    }
}
