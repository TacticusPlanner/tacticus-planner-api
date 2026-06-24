using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class MoWComponent
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("grandAlliance")]
    public string GrandAlliance { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}
