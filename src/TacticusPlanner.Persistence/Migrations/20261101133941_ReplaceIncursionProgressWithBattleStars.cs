using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <summary>
    /// Replaces the per-MoW "tiers cleared" count with per-battle stars. Incursion tiers can be played in any
    /// order, so the old count cannot be mapped to battles; the feature is unreleased, so the old column is
    /// dropped rather than migrated.
    /// </summary>
    public partial class ReplaceIncursionProgressWithBattleStars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "incursion_progress_overrides",
                table: "player_data_overrides");

            migrationBuilder.AddColumn<string>(
                name: "incursion_battle_progress_overrides",
                table: "player_data_overrides",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "incursion_battle_progress_overrides",
                table: "player_data_overrides");

            migrationBuilder.AddColumn<string>(
                name: "incursion_progress_overrides",
                table: "player_data_overrides",
                type: "jsonb",
                nullable: true);
        }
    }
}
