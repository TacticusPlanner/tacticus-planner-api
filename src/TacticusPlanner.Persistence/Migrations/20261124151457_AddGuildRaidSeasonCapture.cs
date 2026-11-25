using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildRaidSeasonCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "capture_not_before",
                table: "guild_raid_sync_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "seasons_planned_for",
                table: "guild_raid_sync_states",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "progression_index",
                table: "guild_raid_attacks",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "tacticus_user_id_suffix",
                table: "guild_raid_attacks",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "guild_raid_season_captures",
                columns: table => new
                {
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    not_found_attempts = table.Column<int>(type: "integer", nullable: false),
                    skipped_entry_count = table.Column<int>(type: "integer", nullable: false),
                    last_attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_raid_season_captures", x => new { x.guild_id, x.season_number });
                    table.ForeignKey(
                        name: "fk_guild_raid_season_captures_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_raid_season_captures");

            migrationBuilder.DropColumn(
                name: "capture_not_before",
                table: "guild_raid_sync_states");

            migrationBuilder.DropColumn(
                name: "seasons_planned_for",
                table: "guild_raid_sync_states");

            migrationBuilder.DropColumn(
                name: "tacticus_user_id_suffix",
                table: "guild_raid_attacks");

            migrationBuilder.AlterColumn<int>(
                name: "progression_index",
                table: "guild_raid_attacks",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
