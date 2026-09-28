using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <summary>
/// Replaces per-project priority (<c>project_goals.priority</c>) with one account-wide order:
/// <c>goals.global_priority</c> for Active/Paused goals plus a per-profile revision
/// (<c>profiles.goal_order_revision</c>). Existing orders are materialized deterministically before the
/// old column is dropped — see <c>openspec/changes/establish-global-goal-priority</c>, decision 5.
/// Back up the database before applying: the old per-project orders are not kept, so rolling back to an
/// old binary needs the backup (<c>Down</c> only reconstructs an approximation from the global order).
/// </summary>
/// <inheritdoc />
public partial class AddGlobalGoalPriority : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "goal_order_revision",
            table: "profiles",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<int>(
            name: "global_priority",
            table: "goals",
            type: "integer",
            nullable: true);

        // Backfill. Each in-flight goal is placed at its first encounter walking the profile's projects:
        // the former Current plan (profiles.active_project_id) first, then the other projects by creation
        // time and id, each in its stored goal order with goal id as the tie-break. In-flight goals
        // without any membership append last by goal creation time and id. updated_at is never used.
        migrationBuilder.Sql(
            """
            WITH first_seen AS (
                SELECT DISTINCT ON (g.id)
                       g.id AS goal_id,
                       CASE WHEN p.id = pr.active_project_id THEN 0 ELSE 1 END AS tier,
                       p.created_at AS project_created_at,
                       p.id AS project_id,
                       pg.priority AS stored_priority
                FROM goals g
                JOIN profiles pr ON pr.id = g.profile_id
                JOIN project_goals pg ON pg.goal_id = g.id
                JOIN projects p ON p.id = pg.project_id
                WHERE g.status IN ('Active', 'Paused')
                ORDER BY g.id, tier, p.created_at, p.id, pg.priority, g.id
            ),
            ordered AS (
                SELECT g.id AS goal_id,
                       ROW_NUMBER() OVER (
                           PARTITION BY g.profile_id
                           ORDER BY (fs.goal_id IS NULL), fs.tier, fs.project_created_at, fs.project_id,
                                    fs.stored_priority, g.created_at, g.id) AS position
                FROM goals g
                LEFT JOIN first_seen fs ON fs.goal_id = g.id
                WHERE g.status IN ('Active', 'Paused')
            )
            UPDATE goals
            SET global_priority = ordered.position
            FROM ordered
            WHERE goals.id = ordered.goal_id;
            """);

        // Invariant check: every in-flight goal holds exactly one position, dense per profile.
        migrationBuilder.Sql(
            """
            DO $$
            DECLARE broken integer;
            BEGIN
                SELECT count(*) INTO broken FROM (
                    SELECT profile_id
                    FROM goals
                    GROUP BY profile_id
                    HAVING count(*) FILTER (WHERE status IN ('Active', 'Paused'))
                               <> count(global_priority)
                        OR count(global_priority) <> count(DISTINCT global_priority)
                        OR coalesce(max(global_priority), 0) <> count(global_priority)
                ) violations;
                IF broken > 0 THEN
                    RAISE EXCEPTION 'AddGlobalGoalPriority: % profile(s) failed the global order invariant', broken;
                END IF;
            END $$;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "ck_goals_global_priority_in_flight",
            table: "goals",
            sql: "(status IN ('Active', 'Paused') AND global_priority IS NOT NULL AND global_priority > 0) OR (status NOT IN ('Active', 'Paused') AND global_priority IS NULL)");

        // Deferrable so one transaction can permute or compact positions without a temporary gap; NULLs
        // (historical goals) are distinct, so no partial filter is needed. Named like the model's index.
        migrationBuilder.Sql(
            """
            ALTER TABLE goals
                ADD CONSTRAINT ix_goals_profile_global_priority
                UNIQUE (profile_id, global_priority) DEFERRABLE INITIALLY DEFERRED;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "ck_project_goals_priority_range",
            table: "project_goals");

        migrationBuilder.DropColumn(
            name: "priority",
            table: "project_goals");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "priority",
            table: "project_goals",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        // Approximation only: each project's goals in global order (in-flight first), then the rest.
        migrationBuilder.Sql(
            """
            UPDATE project_goals
            SET priority = ranked.position
            FROM (
                SELECT pg.project_id, pg.goal_id,
                       ROW_NUMBER() OVER (
                           PARTITION BY pg.project_id
                           ORDER BY (g.global_priority IS NULL), g.global_priority, g.created_at, g.id) AS position
                FROM project_goals pg
                JOIN goals g ON g.id = pg.goal_id
            ) ranked
            WHERE project_goals.project_id = ranked.project_id AND project_goals.goal_id = ranked.goal_id;
            """);

        migrationBuilder.Sql("ALTER TABLE project_goals ALTER COLUMN priority DROP DEFAULT;");

        migrationBuilder.AddCheckConstraint(
            name: "ck_project_goals_priority_range",
            table: "project_goals",
            sql: "priority > 0 AND priority <= 10000");

        migrationBuilder.Sql("ALTER TABLE goals DROP CONSTRAINT ix_goals_profile_global_priority;");

        migrationBuilder.DropCheckConstraint(
            name: "ck_goals_global_priority_in_flight",
            table: "goals");

        migrationBuilder.DropColumn(
            name: "goal_order_revision",
            table: "profiles");

        migrationBuilder.DropColumn(
            name: "global_priority",
            table: "goals");
    }
}
