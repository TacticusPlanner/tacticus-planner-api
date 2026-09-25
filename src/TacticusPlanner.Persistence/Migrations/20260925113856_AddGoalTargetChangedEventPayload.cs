using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TacticusPlanner.Persistence.Migrations;

/// <inheritdoc />
/// <remarks>
/// Snapshot-only: the optional previous/new target on a goal event lives inside the existing
/// <c>events</c> jsonb column, so there is no relational change. Existing event rows stay readable — the
/// new fields are simply absent (null). The migration exists so the model snapshot matches the model.
/// </remarks>
public partial class AddGoalTargetChangedEventPayload : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
