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
    /// Manual campaign-event progress layered over the currently synchronized event. Regular and
    /// challenge portions are independently nullable so either can fall back to the synchronized value.
    /// </summary>
    public List<CampaignEventProgressOverrideRecord> CampaignEventProgressOverrides { get; set; } = [];

    /// <summary>
    /// The player's current Onslaught sector/tier for each alliance. The public player API does not
    /// expose this progress, so Ascension estimates resolve it from this manual override.
    /// </summary>
    public List<OnslaughtProgressOverrideRecord> OnslaughtProgressOverrides { get; set; } = [];

    /// <summary>
    /// Per-battle progress the public player API does not expose: medals earned, Lightning Victory, and
    /// the campaign-event milestone tier. A battle with fewer than 3 medals cannot be raided, so the
    /// planner marks it as one to play manually.
    /// </summary>
    public List<BattleProgressOverrideRecord> BattleProgressOverrides { get; set; } = [];

    // LRE overrides are intentionally excluded for now — the shape of LRE planning annotations
    // (e.g. "maybe"/"stop" per encounter) isn't settled yet; add them back once it is.

    public virtual Profile? Profile { get; set; }
}

public sealed class CampaignEventProgressOverrideRecord
{
    /// <summary>The catalog/Tacticus campaign-event group id.</summary>
    public CampaignId CampaignGroupId { get; set; } = CampaignId.From(string.Empty);

    /// <summary>Standard or Extremis.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Null means use the synchronized regular-track value.</summary>
    public int? CompletedBattleCount { get; set; }

    /// <summary>Null means use the synchronized challenge ids.</summary>
    public List<string>? CompletedChallengeBattlesIds { get; set; }
}

public sealed class OnslaughtProgressOverrideRecord
{
    /// <summary>Imperial, Xenos, or Chaos.</summary>
    public string Alliance { get; set; } = string.Empty;

    /// <summary>Stone, Iron, Bronze, Silver, Gold, Diamond, or Adamantine.</summary>
    public string Sector { get; set; } = "Stone";

    /// <summary>The current position within the sector (1-4). Tier 4 means the sector is complete
    /// and uses tier-3 rewards for planning estimates.</summary>
    public int Tier { get; set; } = 1;
}

public sealed class BattleProgressOverrideRecord
{
    /// <summary>The catalog campaign battle id (e.g. <c>FoCE38</c>).</summary>
    public string BattleId { get; set; } = string.Empty;

    /// <summary>Medals earned on the battle (0-3). Below 3 the battle cannot be raided.</summary>
    public int Medals { get; set; }

    /// <summary>Whether a Lightning Victory was achieved; null means the default, achieved wherever the battle
    /// has one.</summary>
    public bool? LightningVictory { get; set; }

    /// <summary>Campaign-event milestone tier earned (None, Bronze, Silver, or Gold); null means the default,
    /// the best tier the battle awards. Event battles only.</summary>
    public string? Milestone { get; set; }
}
