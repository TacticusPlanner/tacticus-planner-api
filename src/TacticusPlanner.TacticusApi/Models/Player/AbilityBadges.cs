using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class AbilityBadge
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("rarity")]
    public string Rarity { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

public class AbilityBadges
{
    [JsonPropertyName("Imperial")]
    public IEnumerable<AbilityBadge> Imperial { get; set; }

    [JsonPropertyName("Xenos")]
    public IEnumerable<AbilityBadge> Xenos { get; set; }

    [JsonPropertyName("Chaos")]
    public IEnumerable<AbilityBadge> Chaos { get; set; }
}
