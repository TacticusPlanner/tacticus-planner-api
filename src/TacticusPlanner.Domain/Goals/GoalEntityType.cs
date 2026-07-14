namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// The category of thing a goal targets. Stored as its own column rather than inferred from
/// <see cref="GoalType"/> — see the V2 Goals plan §3.1 ("do not infer the entity category exclusively
/// from the goal type").
/// </summary>
public enum GoalEntityType
{
    Character,
    Mow,

    /// <summary>Reserved for the deferred material-goal types (V1's UpgradeMaterial/PreFarmMaterialForGoals).
    /// No goal may use this value yet — see the plan's §4 "Deferred goal types".</summary>
    Upgrade,
}
