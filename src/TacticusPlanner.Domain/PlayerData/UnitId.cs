using Vogen;

namespace TacticusPlanner.Domain.PlayerData;

[ValueObject<string>(conversions: Conversions.SystemTextJson)]
public readonly partial struct UnitId;
