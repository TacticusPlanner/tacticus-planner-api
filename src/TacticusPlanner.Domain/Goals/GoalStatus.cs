namespace TacticusPlanner.Domain.Goals;

/// <summary>Persisted as a string (see <c>GoalConfiguration.HasConversion&lt;string&gt;()</c>); explicit
/// values are future-proofing only, not a storage requirement. No <c>Draft</c> or <c>Deleted</c> value — a
/// goal is either a real record from the moment it's created, or hard-deleted (see
/// <c>DeleteGoalEndpoint</c>), never soft-deleted via status.</summary>
public enum GoalStatus
{
    Active = 1,
    Paused = 2,
    Completed = 3,
    Archived = 4,
}
