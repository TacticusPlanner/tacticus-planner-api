using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPlayerData : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "player_data_overrides",
            columns: table => new
            {
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                battle_result_overrides = table.Column<string>(type: "jsonb", nullable: true),
                campaign_progress_overrides = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_player_data_overrides", x => x.profile_id);
                table.ForeignKey(
                    name: "FK_player_data_overrides_profiles_profile_id",
                    column: x => x.profile_id,
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "player_data_snapshots",
            columns: table => new
            {
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                config_hash = table.Column<string>(type: "text", nullable: false),
                tacticus_last_updated_on = table.Column<long>(type: "bigint", nullable: false),
                source_hash = table.Column<string>(type: "text", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false),
                chunk_hashes = table.Column<string>(type: "jsonb", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                campaign_events_progress = table.Column<string>(type: "jsonb", nullable: true),
                campaign_progress = table.Column<string>(type: "jsonb", nullable: true),
                characters = table.Column<string>(type: "jsonb", nullable: true),
                inventory = table.Column<string>(type: "jsonb", nullable: false),
                inventory_items = table.Column<string>(type: "jsonb", nullable: true),
                inventory_upgrades = table.Column<string>(type: "jsonb", nullable: true),
                live_progress = table.Column<string>(type: "jsonb", nullable: false),
                lre_progress = table.Column<string>(type: "jsonb", nullable: true),
                mows = table.Column<string>(type: "jsonb", nullable: true),
                player_details = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_player_data_snapshots", x => x.profile_id);
                table.ForeignKey(
                    name: "FK_player_data_snapshots_profiles_profile_id",
                    column: x => x.profile_id,
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "player_data_overrides");

        migrationBuilder.DropTable(
            name: "player_data_snapshots");
    }
}
