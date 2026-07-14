using Vogen;

namespace TacticusPlanner.Domain.Goals;

/// <summary>A campaign battle/node id, as used by the frontend's farming-location catalog (an opaque
/// string code, not a composite/encoded value — see <c>battleIdSchema</c> in <c>@workspace/game-domain</c>).</summary>
[ValueObject<string>(conversions: Conversions.SystemTextJson)]
public readonly partial struct CampaignBattleId;
