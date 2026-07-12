using TacticusPlanner.Domain.Common;
using Vogen;

namespace TacticusPlanner.Domain.Guilds;

[ValueObject<Guid>]
public readonly partial struct GuildMemberId : IGuidValueObject;
