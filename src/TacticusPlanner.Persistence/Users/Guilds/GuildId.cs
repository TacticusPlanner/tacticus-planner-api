using TacticusPlanner.Persistence.Model;
using Vogen;

namespace TacticusPlanner.Persistence.Users.Guilds;

[ValueObject<Guid>]
public readonly partial struct GuildId : IGuidValueObject;
