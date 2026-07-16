using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
public partial class RemoveGoalOnslaughtAssumptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM project_goals
            WHERE goal_id IN (SELECT id FROM goals WHERE entity_type = 'Upgrade');

            DELETE FROM goals WHERE entity_type = 'Upgrade';

            UPDATE goals
            SET config = config
                #- '{AscensionFarming,OnslaughtSector}'
                #- '{AscensionFarming,OnslaughtTier}'
                #- '{ascensionFarming,onslaughtSector}'
                #- '{ascensionFarming,onslaughtTier}'
            WHERE config IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Removed per-goal assumptions cannot be reconstructed. Profile overrides remain intact.
    }
}
