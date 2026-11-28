using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildRaidMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ended_season_attempted_at",
                table: "guild_raid_sync_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ended_season_not_found_count",
                table: "guild_raid_sync_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ended_season_number",
                table: "guild_raid_sync_states",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "finalized_at",
                table: "guild_raid_seasons",
                type: "timestamp with time zone",
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

            migrationBuilder.AddColumn<Guid>(
                name: "rejected_profile_id",
                table: "guild_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "verification_method",
                table: "guild_members",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_at",
                table: "guild_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "verified_profile_id",
                table: "guild_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "guild_players",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tacticus_user_id_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    tacticus_user_id_suffix = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    assigned_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_players", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_players_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guild_players_guild_id_tacticus_user_id_hash",
                table: "guild_players",
                columns: new[] { "guild_id", "tacticus_user_id_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_players");

            migrationBuilder.DropColumn(
                name: "ended_season_attempted_at",
                table: "guild_raid_sync_states");

            migrationBuilder.DropColumn(
                name: "ended_season_not_found_count",
                table: "guild_raid_sync_states");

            migrationBuilder.DropColumn(
                name: "ended_season_number",
                table: "guild_raid_sync_states");

            migrationBuilder.DropColumn(
                name: "finalized_at",
                table: "guild_raid_seasons");

            migrationBuilder.DropColumn(
                name: "tacticus_user_id_suffix",
                table: "guild_raid_attacks");

            migrationBuilder.DropColumn(
                name: "rejected_profile_id",
                table: "guild_members");

            migrationBuilder.DropColumn(
                name: "verification_method",
                table: "guild_members");

            migrationBuilder.DropColumn(
                name: "verified_at",
                table: "guild_members");

            migrationBuilder.DropColumn(
                name: "verified_profile_id",
                table: "guild_members");

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
