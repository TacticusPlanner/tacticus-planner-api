using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildWars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guild_wars",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    battlefield_level = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_wars", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_wars_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guild_war_attacks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    war_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attacked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    rarity_cap = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    units_lost = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enemy = table.Column<string>(type: "jsonb", nullable: true),
                    lineup = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_war_attacks", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_war_attacks_guild_wars_war_id",
                        column: x => x.war_id,
                        principalTable: "guild_wars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guild_war_attacks_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guild_war_attacks_profile_id",
                table: "guild_war_attacks",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_war_attacks_war_id",
                table: "guild_war_attacks",
                column: "war_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_wars_profile_id_started_at_id",
                table: "guild_wars",
                columns: new[] { "profile_id", "started_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ux_guild_wars_profile_id_current",
                table: "guild_wars",
                column: "profile_id",
                unique: true,
                filter: "ended_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_war_attacks");

            migrationBuilder.DropTable(
                name: "guild_wars");
        }
    }
}
