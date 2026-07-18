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

    /// <summary>An equipment/relic catalog id (the game-catalog <c>equipment</c> dataset's id) —
    /// unlike Character/Mow this isn't synced player-owned-unit state directly; progress is matched
    /// against the player-data <c>inventory-items</c> chunk instead. Only valid with
    /// <see cref="GoalType.UpgradeEquipment"/>.</summary>
    Equipment,
}
