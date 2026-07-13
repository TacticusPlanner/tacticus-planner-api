using Vogen;

namespace TacticusPlanner.Domain.Guilds;

[ValueObject<string>(conversions: Conversions.SystemTextJson)]
public readonly partial struct TacticusGuildId;
