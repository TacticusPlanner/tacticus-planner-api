namespace TacticusPlanner.Domain.Goals;

/// <summary>The kind of progression or action a goal requests for its unit.</summary>
public enum GoalType
{
    Rank = 1,
    Ascension = 2,
    Ability = 3,
    Unlock = 4,
    Upgrade = 5,
    Level = 7,
}
