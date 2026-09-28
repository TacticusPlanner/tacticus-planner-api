using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class RemoveLevelGoals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The standalone Level goal type is removed (openspec change integrate-level-progression-into-rank-goals):
        // a Rank/Ability goal carries its required level itself. First drop every dependency edge pointing
        // at a Level goal (bumping the revision so an open editor is not silently stale), then delete the
        // Level goals; their project memberships go with them through the ON DELETE CASCADE foreign key.
        migrationBuilder.Sql("""
            UPDATE goals AS goal
            SET depends_on = ARRAY(
                    SELECT dependency
                    FROM unnest(goal.depends_on) AS dependency
                    WHERE dependency NOT IN (SELECT id FROM goals WHERE goal_type = 'Level')),
                revision = goal.revision + 1,
                updated_at = now()
            WHERE goal.goal_type <> 'Level'
              AND goal.depends_on && ARRAY(SELECT id FROM goals WHERE goal_type = 'Level');
            """);

        migrationBuilder.Sql("DELETE FROM goals WHERE goal_type = 'Level';");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The deleted Level goals are not recoverable; the goal type itself no longer exists in code.
    }
}
