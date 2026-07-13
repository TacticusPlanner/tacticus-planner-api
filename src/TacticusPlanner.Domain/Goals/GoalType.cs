namespace TacticusPlanner.Domain.Goals;

/// <summary>The kind of progression or action a goal requests for its entity.</summary>
public enum GoalType
{
    Rank,
    Ascension,
    Ability,
    Unlock,
    Shards,

    /// <summary>Reserved for the deferred material-goal types. No goal may use this value yet — see the
    /// plan's §4 "Deferred goal types".</summary>
    Material,
}
