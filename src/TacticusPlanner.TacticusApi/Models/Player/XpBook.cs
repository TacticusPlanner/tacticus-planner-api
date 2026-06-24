using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class XpBook
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("rarity")]
    public string Name { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}
