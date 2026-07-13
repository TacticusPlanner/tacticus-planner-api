using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.Goals;

/// <summary>
/// A user-authored planning target for a character, Machine of War, or (reserved) upgrade material.
/// Persisted in its own table, not folded into <c>PlayerDataOverride</c> — goals are user-owned planning
/// data, not synced/overridden player state (see the V2 Goals plan §3).
/// </summary>
public class Goal : BaseEntity<GoalId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public ProfileId ProfileId { get; set; }

    public GoalEntityType EntityType { get; set; }

    /// <summary>unitId / mowId / (materialId, reserved) — the id of the thing this goal targets.</summary>
    public required string EntityId { get; set; }

    public GoalType GoalType { get; set; }

    public GoalStatus Status { get; set; }

    /// <summary>Freeform user notes — editable after creation, unlike the target fields in <see cref="Config"/>.</summary>
    public string? Notes { get; set; }

    public GoalConfig Config { get; set; } = new();

    public List<GoalMilestone> Milestones { get; set; } = [];

    /// <summary>Null until the estimation engine populates it at creation time (a later phase).</summary>
    public GoalSnapshot? Snapshot { get; set; }

    public List<GoalEvent> Events { get; set; } = [];

    /// <summary>Prerequisite edges from combined creation (unlock -> ascend -> rank). Empty until the
    /// combined-creation flow (a later phase) links goals together.</summary>
    public List<Guid> DependsOn { get; set; } = [];

    /// <summary>Groups goals created together in one combined-creation flow. Null for goals created
    /// individually.</summary>
    public Guid? AggregateId { get; set; }

    public virtual Profile? Profile { get; set; }
}
