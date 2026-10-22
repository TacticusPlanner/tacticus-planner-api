using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    issuer = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    tacticus_user_id = table.Column<string>(type: "text", nullable: true),
                    tacticus_user_id_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    goal_order_revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profiles", x => x.id);
                });

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
                    global_priority = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    depends_on = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    config = table.Column<string>(type: "jsonb", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goals", x => x.id);
                    table.CheckConstraint("ck_goals_global_priority_in_flight", "(status IN ('Active', 'Paused') AND global_priority IS NOT NULL AND global_priority > 0) OR (status NOT IN ('Active', 'Paused') AND global_priority IS NULL)");
                    table.ForeignKey(
                        name: "fk_goals_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guilds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    tacticus_guild_id = table.Column<string>(type: "text", nullable: false),
                    tacticus_guild_id_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
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
                    table.PrimaryKey("pk_guilds", x => x.id);
                    table.ForeignKey(
                        name: "fk_guilds_profiles_configured_by_profile_id",
                        column: x => x.configured_by_profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "legendary_event_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    catalog_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    show_paid_options = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    annotations = table.Column<string>(type: "jsonb", nullable: true),
                    runs = table.Column<string>(type: "jsonb", nullable: true),
                    teams = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legendary_event_plans", x => x.id);
                    table.ForeignKey(
                        name: "fk_legendary_event_plans_profiles_profile_id",
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
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lanes = table.Column<string>(type: "jsonb", nullable: false),
                    roster = table.Column<string>(type: "jsonb", nullable: false),
                    teams = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legendary_event_run_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_legendary_event_run_records_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_data_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    campaign_event_progress_overrides = table.Column<string>(type: "jsonb", nullable: true),
                    onslaught_progress_overrides = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_data_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_player_data_overrides_profiles_id",
                        column: x => x.id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_data_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_hash = table.Column<string>(type: "text", nullable: false),
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
                    inventory_shards = table.Column<string>(type: "jsonb", nullable: true),
                    inventory_upgrades = table.Column<string>(type: "jsonb", nullable: true),
                    live_progress = table.Column<string>(type: "jsonb", nullable: false),
                    lre_progress = table.Column<string>(type: "jsonb", nullable: true),
                    mows = table.Column<string>(type: "jsonb", nullable: true),
                    player_details = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_data_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_player_data_snapshots_profiles_id",
                        column: x => x.id,
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
                    target_kind = table.Column<string>(type: "text", nullable: true),
                    target_event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                name: "tacticus_integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tacticus_api_key = table.Column<string>(type: "text", nullable: true),
                    tacticus_sync_last_attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tacticus_sync_last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tacticus_integrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_tacticus_integrations_profiles_id",
                        column: x => x.id,
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
                name: "guild_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tacticus_user_id = table.Column<string>(type: "text", nullable: false),
                    tacticus_user_id_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    last_active_in_game_on = table.Column<long>(type: "bigint", nullable: true),
                    last_active_in_planner_on = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    linked_player_name = table.Column<string>(type: "text", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_guild_members_guilds_guild_id",
                        column: x => x.guild_id,
                        principalTable: "guilds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guild_members_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

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
                name: "project_goals",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_goals", x => new { x.project_id, x.goal_id });
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
                    units = table.Column<string>(type: "jsonb", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "ix_goals_profile_id",
                table: "goals",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_guild_members_guild_id_tacticus_user_id_hash",
                table: "guild_members",
                columns: new[] { "guild_id", "tacticus_user_id_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guild_members_profile_id",
                table: "guild_members",
                column: "profile_id",
                unique: true,
                filter: "profile_id IS NOT NULL");

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

            migrationBuilder.CreateIndex(
                name: "ix_guilds_configured_by_profile_id",
                table: "guilds",
                column: "configured_by_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_guilds_tacticus_guild_id_hash",
                table: "guilds",
                column: "tacticus_guild_id_hash",
                unique: true,
                filter: "tacticus_guild_id_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_guilds_tag",
                table: "guilds",
                column: "tag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legendary_event_plans_profile_id_event_id",
                table: "legendary_event_plans",
                columns: new[] { "profile_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legendary_event_run_records_profile_id_event_id_started_at_id",
                table: "legendary_event_run_records",
                columns: new[] { "profile_id", "event_id", "started_at", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_profiles_issuer_subject",
                table: "profiles",
                columns: new[] { "issuer", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profiles_tacticus_user_id_hash",
                table: "profiles",
                column: "tacticus_user_id_hash",
                unique: true,
                filter: "tacticus_user_id_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_project_goals_goal_id",
                table: "project_goals",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_profile_id_default",
                table: "projects",
                column: "profile_id",
                unique: true,
                filter: "type = 'Default'");

            migrationBuilder.CreateIndex(
                name: "ix_projects_profile_id_target_kind_target_event_id",
                table: "projects",
                columns: new[] { "profile_id", "target_kind", "target_event_id" });

            // Not expressible in the EF model (see GoalConfiguration): one position per in-flight goal per profile,
            // checked at commit so a reorder can permute positions inside one transaction. Guarded by
            // GlobalPriorityConstraintTests; a future migration squash must keep this statement.
            migrationBuilder.Sql(
                "ALTER TABLE goals ADD CONSTRAINT ix_goals_profile_global_priority "
                + "UNIQUE (profile_id, global_priority) DEFERRABLE INITIALLY DEFERRED;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_members");

            migrationBuilder.DropTable(
                name: "guild_raid_attacks");

            migrationBuilder.DropTable(
                name: "guild_raid_sync_states");

            migrationBuilder.DropTable(
                name: "legendary_event_plans");

            migrationBuilder.DropTable(
                name: "legendary_event_run_records");

            migrationBuilder.DropTable(
                name: "player_data_overrides");

            migrationBuilder.DropTable(
                name: "player_data_snapshots");

            migrationBuilder.DropTable(
                name: "project_goals");

            migrationBuilder.DropTable(
                name: "tacticus_integrations");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "guild_raid_seasons");

            migrationBuilder.DropTable(
                name: "goals");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "guilds");

            migrationBuilder.DropTable(
                name: "profiles");
        }
    }
}
