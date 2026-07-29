using System.Text.Json.Serialization;

namespace TacticusPlanner.Domain.Goals;

/// <summary>One append-only lifecycle event kind (plan §10). No <c>Deleted</c> event — deletion is a hard
/// delete of the whole goal row (see <c>DeleteGoalEndpoint</c>), so there is no surviving record to append
/// an event to.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalEventType>))]
public enum GoalEventType
{
    Created = 1,
    Paused = 2,
    Resumed = 3,
    PriorityChanged = 4,
    Completed = 5,
    Archived = 6,
}
