using TacticusPlanner.Domain.Common;
using Vogen;

namespace TacticusPlanner.Domain.Goals;

[ValueObject<Guid>]
public readonly partial struct GoalId : IGuidValueObject;
