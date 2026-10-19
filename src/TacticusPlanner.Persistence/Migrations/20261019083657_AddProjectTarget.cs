using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF generates inline column arrays for composite indexes.

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class AddProjectTarget : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "target_event_id",
            table: "projects",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "target_kind",
            table: "projects",
            type: "text",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_projects_profile_id_target_kind_target_event_id",
            table: "projects",
            columns: new[] { "profile_id", "target_kind", "target_event_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_projects_profile_id_target_kind_target_event_id",
            table: "projects");

        migrationBuilder.DropColumn(
            name: "target_event_id",
            table: "projects");

        migrationBuilder.DropColumn(
            name: "target_kind",
            table: "projects");
    }
}
#pragma warning restore CA1861
