namespace TacticusPlanner.Domain.Goals;

/// <summary>The kind of progression or action a goal requests for its entity.</summary>
public enum GoalType
{
    Rank,
    Ascension,
    Ability,
    Unlock,

    /// <summary>Farm one or more specific upgrade materials to a target quantity, independent of a
    /// Rank/Ability range. <see cref="GoalEntityType.Character"/>/<see cref="GoalEntityType.Mow"/>
    /// only — see <see cref="GoalConfig.Upgrade"/>.</summary>
    Upgrade,

    /// <summary>Level up a specific piece of equipment/relic gear. <see cref="GoalEntityType.Equipment"/>
    /// only — see <see cref="GoalConfig.Equipment"/>.</summary>
    UpgradeEquipment,
}
