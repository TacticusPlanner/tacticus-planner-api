using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOneActiveOrPausedGoalPerEntityAndTypeIndex : Migration
    {
        private static readonly string[] IndexColumns = ["profile_id", "entity_type", "entity_id", "goal_type"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_goals_one_active_or_paused_per_entity_and_type",
                table: "goals",
                columns: IndexColumns,
                unique: true,
                filter: "status IN ('Active', 'Paused')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_goals_one_active_or_paused_per_entity_and_type",
                table: "goals");
        }
    }
}
