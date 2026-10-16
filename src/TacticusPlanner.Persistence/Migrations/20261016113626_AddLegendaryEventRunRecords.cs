using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // EF generates inline column arrays for composite indexes.

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class AddLegendaryEventRunRecords : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "roster_snapshots",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                catalog_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                characters = table.Column<string>(type: "jsonb", nullable: true),
                mows = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_roster_snapshots", x => x.id);
                table.CheckConstraint("ck_roster_snapshots_name", "name IS NULL OR char_length(name) <= 60");
                table.CheckConstraint("ck_roster_snapshots_source", "source IN ('legendaryEventRun')");
                table.ForeignKey(
                    name: "fk_roster_snapshots_profiles_profile_id",
                    column: x => x.profile_id,
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "legendary_event_run_records",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                run = table.Column<int>(type: "integer", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                progress_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                catalog_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                points = table.Column<int>(type: "integer", nullable: false),
                roster_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lanes = table.Column<string>(type: "jsonb", nullable: false),
                teams = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_legendary_event_run_records", x => x.id);
                table.CheckConstraint("ck_legendary_event_run_records_run", "run BETWEEN 1 AND 3");
                table.ForeignKey(
                    name: "fk_legendary_event_run_records_profiles_profile_id",
                    column: x => x.profile_id,
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_legendary_event_run_records_roster_snapshots_roster_snapsho",
                    column: x => x.roster_snapshot_id,
                    principalTable: "roster_snapshots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "ix_legendary_event_run_records_profile_id_event_id_started_at",
            table: "legendary_event_run_records",
            columns: new[] { "profile_id", "event_id", "started_at" },
            descending: new[] { false, false, true });

        migrationBuilder.CreateIndex(
            name: "ix_legendary_event_run_records_roster_snapshot_id",
            table: "legendary_event_run_records",
            column: "roster_snapshot_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_roster_snapshots_profile_id_taken_at",
            table: "roster_snapshots",
            columns: new[] { "profile_id", "taken_at" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "legendary_event_run_records");

        migrationBuilder.DropTable(
            name: "roster_snapshots");
    }
}
#pragma warning restore CA1861
