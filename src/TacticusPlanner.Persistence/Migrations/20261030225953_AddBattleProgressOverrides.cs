using Microsoft.EntityFrameworkCore.Migrations;
using TacticusPlanner.Persistence.FeatureGates;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    [FeatureGatedMigration(SchemaFeatureFlags.ManualBattles)]
    public partial class AddBattleProgressOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "battle_progress_overrides",
                table: "player_data_overrides",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "battle_progress_overrides",
                table: "player_data_overrides");
        }
    }
}
