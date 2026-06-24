using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class Ability
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>
    /// 0 = ability is locked
    /// </summary>

    [JsonPropertyName("level")]
    public int Level { get; set; }
}
