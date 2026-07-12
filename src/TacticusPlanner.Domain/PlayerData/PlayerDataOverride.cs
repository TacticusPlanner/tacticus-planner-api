using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.PlayerData;

/// <summary>
/// Manual/override player data: information the Tacticus API does not return, or returns only
/// partially, entered or corrected by the user. Never written by the sync flow — see
/// <see cref="PlayerDataSnapshot"/> for API-synced data. One row per profile. Effective player state
/// for a feature is resolved by combining a snapshot with its override at read time; the two are
/// never merged into one stored row (ADR 0007).
/// </summary>
public class PlayerDataOverride : BaseEntity<ProfileId>, IRevisionedEntity
{
    public long Revision { get; set; }

    /// <summary>
    /// Per-battle result overrides. Absence of an entry for a battle means the effective result
    /// assumes the maximum possible outcome for a completed battle (max medals, and — for
    /// campaign-event battles — all applicable completion flags). Only overrides that lower a
    /// result below that maximum need to be stored.
    /// </summary>
    public List<BattleResultOverrideRecord> BattleResultOverrides { get; set; } = [];

    /// <summary>
    /// Manual campaign-progress entries for campaigns the API sync does not cover for this player
    /// (e.g. a non-currently-selected campaign track).
    /// </summary>
    public List<CampaignProgressOverrideRecord> CampaignProgressOverrides { get; set; } = [];

    // LRE overrides are intentionally excluded for now — the shape of LRE planning annotations
    // (e.g. "maybe"/"stop" per encounter) isn't settled yet; add them back once it is.

    public virtual Profile? Profile { get; set; }
}

public sealed class BattleResultOverrideRecord
{
    /// <summary>The Tacticus API's campaign id (matches <c>CampaignProgressRecord.TacticusCampaignId</c>),
    /// e.g. <c>campaign1</c>, <c>eventCampaign6</c>.</summary>
    public CampaignId TacticusCampaignId { get; set; } = CampaignId.From(string.Empty);

    /// <summary>The battle's <c>battleIndex</c> within that campaign, matching the synced progress data —
    /// not the catalog's <c>nodeNumber</c> (see the alignment caveat on
    /// <c>GameCatalogDatasets.CampaignBattleGroups</c>).</summary>
    public int BattleIndex { get; set; }

    /// <summary>Medal result: 1, 2, or 3.</summary>
    public int Medal { get; set; }

    public bool LightningVictory { get; set; }

    /// <summary>Victory without using a borrowed character. Only meaningful for campaign-event battles;
    /// null when not applicable.</summary>
    public bool? VictoryWithoutBorrowed { get; set; }
}

public sealed class CampaignProgressOverrideRecord
{
    /// <summary>The catalog campaign group id this manual entry applies to.</summary>
    public CampaignId CatalogCampaignGroupId { get; set; } = CampaignId.From(string.Empty);

    public int HighestCompletedNodeNumber { get; set; }
}
