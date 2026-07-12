using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
// CA1861 (avoid constant array arguments) fires on the composite-index array literal below; this is
// scaffolded migration code that runs once, not a hot path, so the analyzer's concern doesn't apply.
#pragma warning disable CA1861

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuilds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guilds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    tacticus_guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    guild_api_token = table.Column<string>(type: "text", nullable: true),
                    configured_by_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_sync_attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_sync_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guilds", x => x.id);
                    table.ForeignKey(
                        name: "FK_guilds_profiles_configured_by_profile_id",
                        column: x => x.configured_by_profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "guild_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tacticus_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tacticus_user_id_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    last_activity_on = table.Column<long>(type: "bigint", nullable: true),
                    linked_player_name = table.Column<string>(type: "text", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guild_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_guild_members_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_guild_members_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_guild_members_guild_id_tacticus_user_id",
                table: "guild_members",
                columns: new[] { "guild_id", "tacticus_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_guild_members_profile_id",
                table: "guild_members",
                column: "profile_id",
                unique: true,
                filter: "profile_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_guilds_configured_by_profile_id",
                table: "guilds",
                column: "configured_by_profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_guilds_tacticus_guild_id",
                table: "guilds",
                column: "tacticus_guild_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_guilds_tag",
                table: "guilds",
                column: "tag",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_members");

            migrationBuilder.DropTable(
                name: "guilds");
        }
    }
}
