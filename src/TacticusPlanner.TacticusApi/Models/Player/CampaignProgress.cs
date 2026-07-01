using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class CampaignProgress
{
    [JsonPropertyName("id")]
    public string Id { get; set; }  // Example: "campaign2"

    [JsonPropertyName("name")]
    public string Name { get; set; }  // Example: "Fall of Cadia"

    [JsonPropertyName("type")]
    public string Type { get; set; }  // Example: "Standard"

    [JsonPropertyName("battles")]
    public List<CampaignLevel> Battles { get; set; }  // List of CampaignLevel objects
}
