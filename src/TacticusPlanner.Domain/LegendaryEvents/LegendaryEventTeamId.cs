using TacticusPlanner.Domain.Common;
using Vogen;

namespace TacticusPlanner.Domain.LegendaryEvents;

[ValueObject<Guid>]
public readonly partial struct LegendaryEventTeamId : IGuidValueObject;
