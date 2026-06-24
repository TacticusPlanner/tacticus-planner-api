using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public record TokenProgress
{
    [JsonPropertyName("tokens")]
    public TokenInfo Tokens { get; set; }
}

public record GuildRaidTokenProgress
{
    [JsonPropertyName("tokens")]
    public TokenInfo Tokens { get; set; }
    [JsonPropertyName("bombTokens")]
    public TokenInfo BombTokens { get; set; }
}

public record TokenInfo
{
    [JsonPropertyName("current")]
    public int Current { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }

    [JsonPropertyName("nextTokenInSeconds")]
    public int NextTokenInSeconds { get; set; }

    [JsonPropertyName("regenDelayInSeconds")]
    public int RegenDelayInSeconds { get; set; }
}
