using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class AddGoalsProjectsAndProgress : Migration
{
    private static readonly string[] GoalsOneActiveOrPausedIndexColumns =
        ["profile_id", "entity_type", "entity_id", "goal_type"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_guild_members_guilds_guild_id",
            table: "guild_members");

        migrationBuilder.DropForeignKey(
            name: "FK_guild_members_profiles_profile_id",
            table: "guild_members");

        migrationBuilder.DropForeignKey(
            name: "FK_guilds_profiles_configured_by_profile_id",
            table: "guilds");

        migrationBuilder.DropForeignKey(
            name: "FK_player_data_overrides_profiles_profile_id",
            table: "player_data_overrides");

        migrationBuilder.DropForeignKey(
            name: "FK_player_data_snapshots_profiles_profile_id",
            table: "player_data_snapshots");

        migrationBuilder.DropForeignKey(
            name: "FK_profiles_accounts_account_id",
            table: "profiles");

        migrationBuilder.DropForeignKey(
            name: "FK_tacticus_integrations_profiles_profile_id",
            table: "tacticus_integrations");

        migrationBuilder.DropPrimaryKey(
            name: "PK_tacticus_integrations",
            table: "tacticus_integrations");

        migrationBuilder.DropPrimaryKey(
            name: "PK_profiles",
            table: "profiles");

        migrationBuilder.DropPrimaryKey(
            name: "PK_player_data_snapshots",
            table: "player_data_snapshots");

        migrationBuilder.DropPrimaryKey(
            name: "PK_player_data_overrides",
            table: "player_data_overrides");

        migrationBuilder.DropPrimaryKey(
            name: "PK_guilds",
            table: "guilds");

        migrationBuilder.DropPrimaryKey(
            name: "PK_guild_members",
            table: "guild_members");

        migrationBuilder.DropPrimaryKey(
            name: "PK_accounts",
            table: "accounts");

        migrationBuilder.RenameColumn(
            name: "profile_id",
            table: "tacticus_integrations",
            newName: "id");

        migrationBuilder.RenameIndex(
            name: "IX_profiles_tacticus_user_id_hash",
            table: "profiles",
            newName: "ix_profiles_tacticus_user_id_hash");

        migrationBuilder.RenameIndex(
            name: "IX_profiles_account_id",
            table: "profiles",
            newName: "ix_profiles_account_id");

        migrationBuilder.RenameColumn(
            name: "profile_id",
            table: "player_data_snapshots",
            newName: "id");

        migrationBuilder.RenameColumn(
            name: "profile_id",
            table: "player_data_overrides",
            newName: "id");

        migrationBuilder.RenameColumn(
            name: "campaign_progress_overrides",
            table: "player_data_overrides",
            newName: "onslaught_progress_overrides");

        migrationBuilder.RenameIndex(
            name: "IX_guilds_tag",
            table: "guilds",
            newName: "ix_guilds_tag");

        migrationBuilder.RenameIndex(
            name: "IX_guilds_tacticus_guild_id_hash",
            table: "guilds",
            newName: "ix_guilds_tacticus_guild_id_hash");

        migrationBuilder.RenameIndex(
            name: "IX_guilds_configured_by_profile_id",
            table: "guilds",
            newName: "ix_guilds_configured_by_profile_id");

        migrationBuilder.RenameIndex(
            name: "IX_guild_members_profile_id",
            table: "guild_members",
            newName: "ix_guild_members_profile_id");

        migrationBuilder.RenameIndex(
            name: "IX_guild_members_guild_id_tacticus_user_id_hash",
            table: "guild_members",
            newName: "ix_guild_members_guild_id_tacticus_user_id_hash");

        migrationBuilder.RenameIndex(
            name: "IX_accounts_issuer_subject",
            table: "accounts",
            newName: "ix_accounts_issuer_subject");

        migrationBuilder.AddColumn<Guid>(
            name: "active_project_id",
            table: "profiles",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "campaign_event_progress_overrides",
            table: "player_data_overrides",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddPrimaryKey(
            name: "pk_tacticus_integrations",
            table: "tacticus_integrations",
            column: "id");

        migrationBuilder.AddPrimaryKey(
            name: "pk_profiles",
            table: "profiles",
            column: "id");

        migrationBuilder.AddPrimaryKey(
            name: "pk_player_data_snapshots",
            table: "player_data_snapshots",
            column: "id");

        migrationBuilder.AddPrimaryKey(
            name: "pk_player_data_overrides",
            table: "player_data_overrides",
            column: "id");

        migrationBuilder.AddPrimaryKey(
            name: "pk_guilds",
            table: "guilds",
            column: "id");

        migrationBuilder.AddPrimaryKey(
            name: "pk_guild_members",
            table: "guild_members",
            column: "id");

        migrationBuilder.AddPrimaryKey(
            name: "pk_accounts",
            table: "accounts",
            column: "id");

        migrationBuilder.CreateTable(
            name: "goals",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false),
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                entity_type = table.Column<string>(type: "text", nullable: false),
                entity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                goal_type = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                depends_on = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                config = table.Column<string>(type: "jsonb", nullable: false),
                events = table.Column<string>(type: "jsonb", nullable: true),
                snapshot = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_goals", x => x.id);
                table.ForeignKey(
                    name: "fk_goals_profiles_profile_id",
                    column: x => x.profile_id,
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "projects",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false),
                profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_projects", x => x.id);
                table.ForeignKey(
                    name: "fk_projects_profiles_profile_id",
                    column: x => x.profile_id,
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_settings",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                settings = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_settings", x => x.id);
                table.ForeignKey(
                    name: "fk_user_settings_profiles_id",
                    column: x => x.id,
                    principalTable: "profiles",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "project_goals",
            columns: table => new
            {
                project_id = table.Column<Guid>(type: "uuid", nullable: false),
                goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_project_goals", x => new { x.project_id, x.goal_id });
                table.CheckConstraint("ck_project_goals_priority_range", "priority > 0 AND priority <= 10000");
                table.ForeignKey(
                    name: "fk_project_goals_goals_goal_id",
                    column: x => x.goal_id,
                    principalTable: "goals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_project_goals_projects_project_id",
                    column: x => x.project_id,
                    principalTable: "projects",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_goals_one_active_or_paused_per_entity_and_type",
            table: "goals",
            columns: GoalsOneActiveOrPausedIndexColumns,
            unique: true,
            filter: "status IN ('Active', 'Paused')");

        migrationBuilder.CreateIndex(
            name: "ix_goals_profile_id",
            table: "goals",
            column: "profile_id");

        migrationBuilder.CreateIndex(
            name: "ix_project_goals_goal_id",
            table: "project_goals",
            column: "goal_id");

        migrationBuilder.CreateIndex(
            name: "ix_projects_profile_id",
            table: "projects",
            column: "profile_id");

        migrationBuilder.AddForeignKey(
            name: "fk_guild_members_guilds_guild_id",
            table: "guild_members",
            column: "guild_id",
            principalTable: "guilds",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_guild_members_profiles_profile_id",
            table: "guild_members",
            column: "profile_id",
            principalTable: "profiles",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_guilds_profiles_configured_by_profile_id",
            table: "guilds",
            column: "configured_by_profile_id",
            principalTable: "profiles",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_player_data_overrides_profiles_id",
            table: "player_data_overrides",
            column: "id",
            principalTable: "profiles",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_player_data_snapshots_profiles_id",
            table: "player_data_snapshots",
            column: "id",
            principalTable: "profiles",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_profiles_accounts_account_id",
            table: "profiles",
            column: "account_id",
            principalTable: "accounts",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_tacticus_integrations_profiles_id",
            table: "tacticus_integrations",
            column: "id",
            principalTable: "profiles",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_guild_members_guilds_guild_id",
            table: "guild_members");

        migrationBuilder.DropForeignKey(
            name: "fk_guild_members_profiles_profile_id",
            table: "guild_members");

        migrationBuilder.DropForeignKey(
            name: "fk_guilds_profiles_configured_by_profile_id",
            table: "guilds");

        migrationBuilder.DropForeignKey(
            name: "fk_player_data_overrides_profiles_id",
            table: "player_data_overrides");

        migrationBuilder.DropForeignKey(
            name: "fk_player_data_snapshots_profiles_id",
            table: "player_data_snapshots");

        migrationBuilder.DropForeignKey(
            name: "fk_profiles_accounts_account_id",
            table: "profiles");

        migrationBuilder.DropForeignKey(
            name: "fk_tacticus_integrations_profiles_id",
            table: "tacticus_integrations");

        migrationBuilder.DropTable(
            name: "project_goals");

        migrationBuilder.DropTable(
            name: "user_settings");

        migrationBuilder.DropTable(
            name: "goals");

        migrationBuilder.DropTable(
            name: "projects");

        migrationBuilder.DropPrimaryKey(
            name: "pk_tacticus_integrations",
            table: "tacticus_integrations");

        migrationBuilder.DropPrimaryKey(
            name: "pk_profiles",
            table: "profiles");

        migrationBuilder.DropPrimaryKey(
            name: "pk_player_data_snapshots",
            table: "player_data_snapshots");

        migrationBuilder.DropPrimaryKey(
            name: "pk_player_data_overrides",
            table: "player_data_overrides");

        migrationBuilder.DropPrimaryKey(
            name: "pk_guilds",
            table: "guilds");

        migrationBuilder.DropPrimaryKey(
            name: "pk_guild_members",
            table: "guild_members");

        migrationBuilder.DropPrimaryKey(
            name: "pk_accounts",
            table: "accounts");

        migrationBuilder.DropColumn(
            name: "active_project_id",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "campaign_event_progress_overrides",
            table: "player_data_overrides");

        migrationBuilder.RenameColumn(
            name: "id",
            table: "tacticus_integrations",
            newName: "profile_id");

        migrationBuilder.RenameIndex(
            name: "ix_profiles_tacticus_user_id_hash",
            table: "profiles",
            newName: "IX_profiles_tacticus_user_id_hash");

        migrationBuilder.RenameIndex(
            name: "ix_profiles_account_id",
            table: "profiles",
            newName: "IX_profiles_account_id");

        migrationBuilder.RenameColumn(
            name: "id",
            table: "player_data_snapshots",
            newName: "profile_id");

        migrationBuilder.RenameColumn(
            name: "id",
            table: "player_data_overrides",
            newName: "profile_id");

        migrationBuilder.RenameColumn(
            name: "onslaught_progress_overrides",
            table: "player_data_overrides",
            newName: "campaign_progress_overrides");

        migrationBuilder.RenameIndex(
            name: "ix_guilds_tag",
            table: "guilds",
            newName: "IX_guilds_tag");

        migrationBuilder.RenameIndex(
            name: "ix_guilds_tacticus_guild_id_hash",
            table: "guilds",
            newName: "IX_guilds_tacticus_guild_id_hash");

        migrationBuilder.RenameIndex(
            name: "ix_guilds_configured_by_profile_id",
            table: "guilds",
            newName: "IX_guilds_configured_by_profile_id");

        migrationBuilder.RenameIndex(
            name: "ix_guild_members_profile_id",
            table: "guild_members",
            newName: "IX_guild_members_profile_id");

        migrationBuilder.RenameIndex(
            name: "ix_guild_members_guild_id_tacticus_user_id_hash",
            table: "guild_members",
            newName: "IX_guild_members_guild_id_tacticus_user_id_hash");

        migrationBuilder.RenameIndex(
            name: "ix_accounts_issuer_subject",
            table: "accounts",
            newName: "IX_accounts_issuer_subject");

        migrationBuilder.AddPrimaryKey(
            name: "PK_tacticus_integrations",
            table: "tacticus_integrations",
            column: "profile_id");

        migrationBuilder.AddPrimaryKey(
            name: "PK_profiles",
            table: "profiles",
            column: "id");

        migrationBuilder.AddPrimaryKey(
            name: "PK_player_data_snapshots",
            table: "player_data_snapshots",
            column: "profile_id");

        migrationBuilder.AddPrimaryKey(
            name: "PK_player_data_overrides",
            table: "player_data_overrides",
            column: "profile_id");

        migrationBuilder.AddPrimaryKey(
            name: "PK_guilds",
            table: "guilds",
            column: "id");

        migrationBuilder.AddPrimaryKey(
            name: "PK_guild_members",
            table: "guild_members",
            column: "id");

        migrationBuilder.AddPrimaryKey(
            name: "PK_accounts",
            table: "accounts",
            column: "id");

        migrationBuilder.AddForeignKey(
            name: "FK_guild_members_guilds_guild_id",
            table: "guild_members",
            column: "guild_id",
            principalTable: "guilds",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_guild_members_profiles_profile_id",
            table: "guild_members",
            column: "profile_id",
            principalTable: "profiles",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_guilds_profiles_configured_by_profile_id",
            table: "guilds",
            column: "configured_by_profile_id",
            principalTable: "profiles",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_player_data_overrides_profiles_profile_id",
            table: "player_data_overrides",
            column: "profile_id",
            principalTable: "profiles",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_player_data_snapshots_profiles_profile_id",
            table: "player_data_snapshots",
            column: "profile_id",
            principalTable: "profiles",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_profiles_accounts_account_id",
            table: "profiles",
            column: "account_id",
            principalTable: "accounts",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_tacticus_integrations_profiles_profile_id",
            table: "tacticus_integrations",
            column: "profile_id",
            principalTable: "profiles",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }
}
