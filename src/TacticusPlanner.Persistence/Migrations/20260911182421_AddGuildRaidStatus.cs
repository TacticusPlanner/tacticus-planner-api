using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
#pragma warning disable CA1861
public partial class AddGuildRaidStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "guild_raid_seasons",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                season_number = table.Column<int>(type: "integer", nullable: false),
                season_config_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_guild_raid_seasons", x => x.id);
                table.ForeignKey(
                    name: "fk_guild_raid_seasons_guilds_guild_id",
                    column: x => x.guild_id,
                    principalTable: "guilds",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "guild_raid_attacks",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                guild_raid_season_id = table.Column<Guid>(type: "uuid", nullable: false),
                content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                tacticus_user_id_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                tier = table.Column<int>(type: "integer", nullable: false),
                set = table.Column<int>(type: "integer", nullable: false),
                encounter_index = table.Column<int>(type: "integer", nullable: false),
                remaining_hp = table.Column<int>(type: "integer", nullable: false),
                maximum_hp = table.Column<int>(type: "integer", nullable: false),
                encounter_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                unit_set_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                progression_index = table.Column<int>(type: "integer", nullable: false),
                difficulty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                damage_dealt = table.Column<int>(type: "integer", nullable: false),
                damage_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_guild_raid_attacks", x => x.id);
                table.ForeignKey(
                    name: "fk_guild_raid_attacks_guild_raid_seasons_guild_raid_season_id",
                    column: x => x.guild_raid_season_id,
                    principalTable: "guild_raid_seasons",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "guild_raid_sync_states",
            columns: table => new
            {
                guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                active_season_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_guild_raid_sync_states", x => x.guild_id);
                table.ForeignKey(
                    name: "fk_guild_raid_sync_states_guild_raid_seasons_active_season_id",
                    column: x => x.active_season_id,
                    principalTable: "guild_raid_seasons",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_guild_raid_sync_states_guilds_guild_id",
                    column: x => x.guild_id,
                    principalTable: "guilds",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "guild_raid_attack_units",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                guild_raid_attack_id = table.Column<Guid>(type: "uuid", nullable: false),
                unit_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                power = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_guild_raid_attack_units", x => x.id);
                table.ForeignKey(
                    name: "fk_guild_raid_attack_units_guild_raid_attacks_guild_raid_attac",
                    column: x => x.guild_raid_attack_id,
                    principalTable: "guild_raid_attacks",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_guild_raid_attack_units_guild_raid_attack_id_kind_position",
            table: "guild_raid_attack_units",
            columns: new[] { "guild_raid_attack_id", "kind", "position" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_guild_raid_attacks_guild_raid_season_id_content_hash",
            table: "guild_raid_attacks",
            columns: new[] { "guild_raid_season_id", "content_hash" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_guild_raid_attacks_guild_raid_season_id_tacticus_user_id_ha",
            table: "guild_raid_attacks",
            columns: new[] { "guild_raid_season_id", "tacticus_user_id_hash", "completed_at" });

        migrationBuilder.CreateIndex(
            name: "ix_guild_raid_seasons_guild_id_season_number",
            table: "guild_raid_seasons",
            columns: new[] { "guild_id", "season_number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_guild_raid_sync_states_active_season_id",
            table: "guild_raid_sync_states",
            column: "active_season_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "guild_raid_attack_units");

        migrationBuilder.DropTable(
            name: "guild_raid_sync_states");

        migrationBuilder.DropTable(
            name: "guild_raid_attacks");

        migrationBuilder.DropTable(
            name: "guild_raid_seasons");
    }
}
#pragma warning restore CA1861
