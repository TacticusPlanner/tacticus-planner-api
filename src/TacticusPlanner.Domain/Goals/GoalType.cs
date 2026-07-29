namespace TacticusPlanner.Domain.Goals;

/// <summary>The kind of progression or action a goal requests for its entity. Persisted as a string;
/// explicit values are future-proofing only.</summary>
public enum GoalType
{
    Rank = 1,
    Ascension = 2,
    Ability = 3,
    Unlock = 4,

    /// <summary>Farm one or more specific upgrade materials to a target quantity, independent of a
    /// Rank/Ability range. <see cref="GoalEntityType.Character"/>/<see cref="GoalEntityType.Mow"/>
    /// only — see <see cref="GoalConfig.Upgrade"/>.</summary>
    Upgrade = 5,

    /// <summary>Level up a specific piece of equipment/relic gear. <see cref="GoalEntityType.Item"/>
    /// only — see <see cref="GoalConfig.Item"/>.</summary>
    UpgradeItem = 6,

    /// <summary>Reach a target character level. Uncosted (no resource estimate — no XP-cost curve
    /// exists yet). <see cref="GoalEntityType.Character"/> only — see
    /// <see cref="GoalConfig.Level"/>.</summary>
    Level = 7,
}
