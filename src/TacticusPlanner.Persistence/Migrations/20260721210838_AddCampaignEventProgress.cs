using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignEventProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "campaign_progress_overrides",
                table: "player_data_overrides",
                newName: "campaign_event_progress_overrides");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "campaign_event_progress_overrides",
                table: "player_data_overrides",
                newName: "campaign_progress_overrides");
        }
    }
}
