using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterSnapshotImportKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "import_key",
                table: "personal_roster_snapshots",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_personal_roster_snapshots_profile_id_import_key",
                table: "personal_roster_snapshots",
                columns: new[] { "profile_id", "import_key" },
                unique: true,
                filter: "import_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_personal_roster_snapshots_profile_id_import_key",
                table: "personal_roster_snapshots");

            migrationBuilder.DropColumn(
                name: "import_key",
                table: "personal_roster_snapshots");
        }
    }
}
