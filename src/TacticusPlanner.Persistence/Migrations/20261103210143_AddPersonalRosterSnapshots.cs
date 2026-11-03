using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalRosterSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personal_roster_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    character_count = table.Column<int>(type: "integer", nullable: false),
                    mow_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    roster = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_personal_roster_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_personal_roster_snapshots_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_personal_roster_snapshots_profile_id_taken_at_id",
                table: "personal_roster_snapshots",
                columns: new[] { "profile_id", "taken_at", "id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personal_roster_snapshots");
        }
    }
}
