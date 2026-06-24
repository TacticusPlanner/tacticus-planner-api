using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public class Unit
{
    [JsonPropertyName("id")] public string Id { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; }

    /// <summary>
    /// Star level: 0 = Common, 3 = Uncommon, 6 = Rare, 9 = Epic, 12 = Legendary
    /// </summary>

    [JsonPropertyName("progressionIndex")]
    public int ProgressionIndex { get; set; }

    /// <summary>
    /// total XP gained for character
    /// </summary>
    [JsonPropertyName("xp")]
    public int Xp { get; set; }

    /// <summary>
    /// XP level of character
    /// </summary>
    [JsonPropertyName("xpLevel")]
    public int XpLevel { get; set; }

    /// <summary>
    /// 0 = Stone I, 3 = Iron I, 6 = Bronze I, 9 = Silver I, 12 = Gold I, 15 = Diamond I, 17 = Diamond III
    /// </summary>
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    /// <summary>
    /// active and passive abilities of character
    /// </summary>
    [JsonPropertyName("abilities")]
    public ICollection<Ability> Abilities { get; set; } = new System.Collections.ObjectModel.Collection<Ability>();

    /// <summary>
    /// 2*3 matrix, 0 = top left, 1 = bottom left, 2 top center etc
    /// </summary>
    [JsonPropertyName("upgrades")]
    public ICollection<int> Upgrades { get; set; } = new System.Collections.ObjectModel.Collection<int>();

    /// <summary>
    /// owned shards of character
    /// </summary>

    [JsonPropertyName("shards")]
    public int Shards { get; set; }

    /// <summary>
    /// owned shards of character
    /// </summary>

    [JsonPropertyName("mythicShards")]
    public int MythicShards { get; set; }

    [JsonPropertyName("items")]
    public ICollection<Equipment> Items { get; set; }
}
