using Vogen;

namespace TacticusPlanner.Domain.Profiles;

[ValueObject<string>(conversions: Conversions.SystemTextJson)]
public readonly partial struct TacticusUserId;
