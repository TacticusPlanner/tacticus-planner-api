using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class Equipment
{
    [JsonPropertyName("slotId")]
    public string SlotId { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("rarity")]
    public string Rarity { get; set; }
}

public class InventoryEquipment
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}
