using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class Upgrade
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}
