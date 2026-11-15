using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// A user-authored planning target for a character or Machine of War.
/// Persisted in its own table, not folded into <c>PlayerDataOverride</c> — goals are user-owned planning
/// data, not synced/overridden player state (see the V2 Goals plan §3).
/// </summary>
public class Goal : BaseEntity<GoalId>, IRevisionedEntity
{
    private Goal()
    {
    }

    public Goal(GoalEntityType entityType, string entityId, GoalType goalType)
    {
        EntityType = entityType;
        EntityId = entityId;
        GoalType = goalType;
    }

    public long Revision { get; set; }

    public ProfileId ProfileId { get; set; }

    public GoalEntityType EntityType { get; private set; }

    /// <summary>The character or Machine of War id targeted by this goal.</summary>
    public string EntityId { get; private set; } = string.Empty;

    public GoalType GoalType { get; private set; }

    public GoalStatus Status { get; set; }

    /// <summary>Position in the account-wide in-flight order (1 = first): set for every Active/Paused goal,
    /// null for Completed/Archived ones. Unique per profile (a deferrable constraint, so a reorder can
    /// permute positions inside one transaction). Only <c>GoalOrderService</c> writes it.</summary>
    public int? GlobalPriority { get; set; }

    /// <summary>Freeform user notes — editable after creation, unlike the target fields in <see cref="Config"/>.</summary>
    public string? Notes { get; set; }

    public GoalConfig Config { get; set; } = new();

    /// <summary>Null until the estimation engine populates it at creation time (a later phase).</summary>
    public GoalSnapshot? Snapshot { get; set; }

    /// <summary>Prerequisite edges from combined creation (unlock -> ascend -> rank). Empty until the
    /// combined-creation flow (a later phase) links goals together.</summary>
    public List<Guid> DependsOn { get; set; } = [];

    /// <summary>Upgrade goals only: the same unit's Rank (Character) or Ability (Machine of War) goals this
    /// goal pre-farms for, in link order. The planner counts the goal's pieces inside those goals' slots toward
    /// them; empty means the implicit link (same unit, overlapping range). Deleting a linked goal removes it
    /// here.</summary>
    public List<Guid> LinkedGoalIds { get; set; } = [];

    public virtual Profile? Profile { get; set; }
}
