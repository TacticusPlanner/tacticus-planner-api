namespace TacticusPlanner.Domain.Guilds;

/// <summary>How a guild member's claim was verified.</summary>
public enum GuildClaimVerificationMethod
{
    /// <summary>The claimant stored the guild's current key while Leader or Co-Leader.</summary>
    Key,

    /// <summary>The claimant's synced Tacticus name equals the name a leader assigned.</summary>
    NameMatch,

    /// <summary>A verified Leader or Co-Leader confirmed the claim.</summary>
    Leader,
}
