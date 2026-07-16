using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class GoalsAdjustments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM goals WHERE goal_type = 'Shards';");

        migrationBuilder.DropColumn(
            name: "ordering",
            table: "planning_settings");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ordering",
            table: "planning_settings",
            type: "text",
            nullable: false,
            defaultValue: "");
    }
}
