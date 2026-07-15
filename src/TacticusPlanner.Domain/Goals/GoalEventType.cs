using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.Goals;

/// <summary>One append-only lifecycle event kind (plan §10).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalEventType>))]
public enum GoalEventType
{
    Created,
    Paused,
    Resumed,
    PriorityChanged,
    Completed,
    Archived,
    Deleted,
}
