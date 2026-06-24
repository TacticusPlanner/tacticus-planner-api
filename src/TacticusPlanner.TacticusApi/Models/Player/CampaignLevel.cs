using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class CampaignLevel
{
    [JsonPropertyName("battleIndex")]
    public int BattleIndex { get; set; }  // Example: 10

    [JsonPropertyName("attemptsLeft")]
    public int AttemptsLeft { get; set; }  // Example: 2

    [JsonPropertyName("attemptsUsed")]
    public int AttemptsUsed { get; set; }  // Example: 3
}
