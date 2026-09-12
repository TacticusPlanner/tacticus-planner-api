using TacticusPlanner.Domain.Common;
using Vogen;

namespace TacticusPlanner.Domain.GuildRaids;

[ValueObject<Guid>]
public readonly partial struct GuildRaidSeasonId : IGuidValueObject;

[ValueObject<Guid>]
public readonly partial struct GuildRaidAttackId : IGuidValueObject;

[ValueObject<Guid>]
public readonly partial struct GuildRaidAttackUnitId : IGuidValueObject;
