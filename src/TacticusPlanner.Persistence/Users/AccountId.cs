using TacticusPlanner.Persistence.Model;
using Vogen;

namespace TacticusPlanner.Persistence.Users;

[ValueObject<Guid>]
public readonly partial struct AccountId : IGuidValueObject;
