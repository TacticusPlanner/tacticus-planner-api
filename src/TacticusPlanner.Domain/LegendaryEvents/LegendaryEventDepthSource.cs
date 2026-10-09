namespace TacticusPlanner.Domain.LegendaryEvents;

/// <summary>Where a team's clear depth for a run came from: the client's estimate, or a value the user
/// typed. Stored as its name; served lower-cased (<c>estimate</c>/<c>manual</c>).</summary>
public enum LegendaryEventDepthSource
{
    Estimate,
    Manual,
}
