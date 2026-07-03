using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    private static readonly string[] AccountIdentityIndexColumns = ["issuer", "subject"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "accounts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                issuer = table.Column<string>(type: "text", nullable: false),
                subject = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_accounts", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "profiles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                display_name = table.Column<string>(type: "text", nullable: false),
                tacticus_user_id = table.Column<string>(type: "text", nullable: true),
                tacticus_user_id_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_profiles", x => x.id);
                table.ForeignKey(
                    name: "FK_profiles_accounts_account_id",
                    column: x => x.account_id,
                    principalTable: "accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "tacticus_integrations",
            columns: table => new
            {
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                tacticus_api_key = table.Column<string>(type: "text", nullable: true),
                tacticus_sync_last_attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                tacticus_sync_last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tacticus_integrations", x => x.profile_id);
                table.ForeignKey(
                    name: "FK_tacticus_integrations_profiles_profile_id",
                    column: x => x.profile_id,
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_accounts_issuer_subject",
            table: "accounts",
            columns: AccountIdentityIndexColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_profiles_account_id",
            table: "profiles",
            column: "account_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_profiles_tacticus_user_id_hash",
            table: "profiles",
            column: "tacticus_user_id_hash",
            unique: true,
            filter: "tacticus_user_id_hash IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "tacticus_integrations");

        migrationBuilder.DropTable(
            name: "profiles");

        migrationBuilder.DropTable(
            name: "accounts");
    }
}
