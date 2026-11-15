using TacticusPlanner.Domain.Common;
using Vogen;

namespace TacticusPlanner.Domain.EventShops;

[ValueObject<Guid>]
public readonly partial struct EventShopPlanId : IGuidValueObject;
