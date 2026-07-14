using TacticusPlanner.Domain.Common;
using Vogen;

namespace TacticusPlanner.Domain.Projects;

[ValueObject<Guid>]
public readonly partial struct ProjectId : IGuidValueObject;
