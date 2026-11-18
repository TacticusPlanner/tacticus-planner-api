using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    import_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    modes = table.Column<string[]>(type: "text[]", nullable: false),
                    character_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    flex_start_index = table.Column<int>(type: "integer", nullable: true),
                    mow_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    campaign_group_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    incursion_mow_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                    table.ForeignKey(
                        name: "fk_teams_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_teams_profile_id_import_key",
                table: "teams",
                columns: new[] { "profile_id", "import_key" },
                unique: true,
                filter: "import_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_teams_profile_id_name_key",
                table: "teams",
                columns: new[] { "profile_id", "name_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teams");
        }
    }
}
