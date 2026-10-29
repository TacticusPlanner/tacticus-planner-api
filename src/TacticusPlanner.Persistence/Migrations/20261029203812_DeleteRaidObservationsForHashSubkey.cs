using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeleteRaidObservationsForHashSubkey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Raid attacker hashes have no stored plaintext, so they can't be re-hashed with the new HMAC
            // subkey. Drop the observations (attacks cascade from seasons); the next refresh re-observes them.
            migrationBuilder.Sql("DELETE FROM guild_raid_sync_states;");
            migrationBuilder.Sql("DELETE FROM guild_raid_seasons;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
