using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class Orb
{
    [JsonPropertyName("rarity")]
    public string Rarity { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

public class Orbs
{
    [JsonPropertyName("Imperial")]
    public IEnumerable<Orb> Imperial { get; set; }

    [JsonPropertyName("Xenos")]
    public IEnumerable<Orb> Xenos { get; set; }

    [JsonPropertyName("Chaos")]
    public IEnumerable<Orb> Chaos { get; set; }
}
