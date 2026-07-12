namespace TacticusPlanner.Domain.PlayerData.Chunks;

public sealed class GuildRaidTokensRecord
{
    public TokenBucketRecord Tokens { get; set; } = new();

    public TokenBucketRecord BombTokens { get; set; } = new();
}

public sealed class TokenBucketRecord
{
    public int Current { get; set; }

    public int Max { get; set; }

    public int NextTokenInSeconds { get; set; }

    public int RegenDelayInSeconds { get; set; }
}

public sealed class GameModeTokensChunk
{
    public TokenBucketRecord? Arena { get; set; }

    public GuildRaidTokensRecord? GuildRaid { get; set; }

    public TokenBucketRecord? Onslaught { get; set; }

    public TokenBucketRecord? SalvageRun { get; set; }
}
