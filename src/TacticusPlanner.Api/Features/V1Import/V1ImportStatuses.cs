namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>The outcome of one import part (<see cref="ImportPartResult"/>) or one V1 Legendary Event
/// (<see cref="V1LegendaryEventOutcome"/>).</summary>
public enum ImportPartStatus
{
    Imported,
    Skipped,
    Failed,
}

/// <summary>The outcome of one V1 goal (<see cref="V1GoalOutcome"/>).</summary>
public enum V1GoalOutcomeStatus
{
    Created,
    Skipped,
    Failed,
}
