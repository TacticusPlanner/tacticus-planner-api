namespace TacticusPlanner.Domain.PlayerData.Chunks;

/// <summary>
/// One campaign's progress identity. Used by both the <c>campaign-progress</c> chunk (standard/
/// mirror/elite/elite-mirror) and the <c>campaign-events-progress</c> chunk (limited-time campaign
/// events). <c>TacticusCampaignId</c> is unconditionally also the static catalog's campaign group id
/// — every Tacticus campaign id now has a matching catalog group (see
/// <c>GameCatalogDatasets.CampaignBattleGroups</c>), so no separate cross-reference field is needed.
/// Per-battle attempt data lives in the <c>live-progress</c> chunk instead (it changes far more often
/// than this identity/high-water-mark record does).
/// </summary>
public sealed class CampaignProgressRecord
{
    /// <summary>The Tacticus API's own campaign id — also the catalog's groupId (e.g. <c>campaign1</c>,
    /// <c>mirror1</c>, <c>elite1</c>, <c>eliteMirror1</c>, <c>eventCampaign6</c>).</summary>
    public CampaignId TacticusCampaignId { get; set; } = CampaignId.From(string.Empty);

    /// <summary>Tacticus campaign type: Standard/Mirror/Elite/EliteMirror for storylines, Standard/Extremis
    /// for campaign events.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The highest battle index the player has completed in this campaign.</summary>
    public int HighestCompletedBattleIndex { get; set; }
}

/// <summary>
/// One campaign-event type's progress. Event challenge battles are optional and can be completed in
/// any order, so their exact catalog battle ids are retained rather than collapsed into a count or
/// inferred from the regular-track high-water mark.
/// </summary>
public sealed class CampaignEventProgressRecord
{
    public CampaignId TacticusCampaignId { get; set; } = CampaignId.From(string.Empty);

    /// <summary>Standard or Extremis.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Number of sequential, non-challenge battles completed.</summary>
    public int CompletedBattleCount { get; set; }

    /// <summary>Exact ids of completed optional challenge battles.</summary>
    public List<string> CompletedChallengeBattlesIds { get; set; } = [];
}
