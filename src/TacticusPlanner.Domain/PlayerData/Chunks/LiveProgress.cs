namespace TacticusPlanner.Domain.PlayerData.Chunks;

/// <summary>Often-changing data kept in its own chunk (<c>live-progress</c>) so it can be re-synced/
/// re-stored independently of the much-less-volatile roster/inventory/campaign-identity chunks.</summary>
public sealed class LiveProgressChunk
{
    /// <summary>Per-battle attempt counters, derived from campaign progress. Replaces the battle list that
    /// used to live on <see cref="CampaignProgressRecord"/> — this changes daily, that doesn't.</summary>
    public List<BattleAttemptRecord> BattleAttempts { get; set; } = [];

    /// <summary>The <see cref="CampaignProgressRecord.TacticusCampaignId"/> of whichever event-type
    /// campaign is currently present in the synced response, if any.</summary>
    public CampaignId? ActiveCampaignEventId { get; set; }

    public GameModeTokensChunk GameModeTokens { get; set; } = new();
}

public sealed class BattleAttemptRecord
{
    /// <summary>Matches <see cref="CampaignProgressRecord.TacticusCampaignId"/>.</summary>
    public CampaignId TacticusCampaignId { get; set; } = CampaignId.From(string.Empty);

    public int BattleIndex { get; set; }

    public int AttemptsLeft { get; set; }

    public int AttemptsUsed { get; set; }
}
