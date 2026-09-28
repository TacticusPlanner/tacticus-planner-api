using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <summary>
/// Removes the "active project" (Current plan) pointer: drops <c>profiles.active_project_id</c> and makes
/// "exactly one Default project per profile" a database invariant with a partial unique index. Before the
/// index is created, every profile without a Default project gets one and any profile with several keeps
/// only the oldest (the others become Custom — never deleted, so goals and memberships are untouched).
/// Users whose active project was not the default lose only that browsing preference; <c>Down</c>
/// re-adds the column pointing at each profile's Default project, not at their former selection. See
/// <c>openspec/changes/consolidate-goals-into-plan-and-remove-active-project</c>.
/// </summary>
/// <inheritdoc />
public partial class RemoveActiveProject : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE projects
            SET type = 'Custom'
            WHERE type = 'Default'
              AND id NOT IN (
                  SELECT DISTINCT ON (profile_id) id
                  FROM projects
                  WHERE type = 'Default'
                  ORDER BY profile_id, created_at, id);
            """);

        migrationBuilder.Sql(
            """
            INSERT INTO projects (id, profile_id, name, status, type, revision, created_at, updated_at)
            SELECT gen_random_uuid(), pr.id, 'My Goals', 'Active', 'Default', 0, now(), now()
            FROM profiles pr
            WHERE NOT EXISTS (
                SELECT 1 FROM projects p WHERE p.profile_id = pr.id AND p.type = 'Default');
            """);

        migrationBuilder.DropColumn(
            name: "active_project_id",
            table: "profiles");

        migrationBuilder.CreateIndex(
            name: "ix_projects_profile_id_default",
            table: "projects",
            column: "profile_id",
            unique: true,
            filter: "type = 'Default'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_projects_profile_id_default",
            table: "projects");

        migrationBuilder.AddColumn<Guid>(
            name: "active_project_id",
            table: "profiles",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE profiles
            SET active_project_id = p.id
            FROM projects p
            WHERE p.profile_id = profiles.id AND p.type = 'Default';
            """);
    }
}
