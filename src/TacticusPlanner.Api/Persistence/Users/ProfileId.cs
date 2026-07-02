using TacticusPlanner.Api.Persistence.Model;
using Vogen;

namespace TacticusPlanner.Api.Persistence.Users;

[ValueObject<Guid>]
public readonly partial struct ProfileId : IGuidValueObject;
