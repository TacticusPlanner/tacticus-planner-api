using TacticusPlanner.Domain.Common;
using Vogen;

namespace TacticusPlanner.Domain.Accounts;

[ValueObject<Guid>]
public readonly partial struct AccountId : IGuidValueObject;
