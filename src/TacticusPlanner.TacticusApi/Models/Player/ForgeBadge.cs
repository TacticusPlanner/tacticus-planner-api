using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class ForgeBadge
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("rarity")]
    public string Rarity { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}
