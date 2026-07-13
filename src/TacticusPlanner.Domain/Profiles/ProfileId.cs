using TacticusPlanner.Domain.Common;
using Vogen;

namespace TacticusPlanner.Domain.Profiles;

[ValueObject<Guid>]
public readonly partial struct ProfileId : IGuidValueObject;
