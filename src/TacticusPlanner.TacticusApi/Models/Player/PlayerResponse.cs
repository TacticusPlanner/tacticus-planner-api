using System.Text.Json.Serialization;

namespace TacticusPlanner.TacticusApi.Models.Player;

public record PlayerResponse
{
    [JsonPropertyName("player")]
    public Player Player { get; set; }

    [JsonPropertyName("metaData")]
    public Metadata Metadata { get; set; }
}

public record PlayerDetails
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("powerLevel")]
    public int PowerLevel { get; set; }
}

public record Player
{
    [JsonPropertyName("details")]
    public PlayerDetails Details { get; set; }

    [JsonPropertyName("units")]
    public IEnumerable<Unit> Units { get; set; }

    [JsonPropertyName("inventory")]
    public Inventory Inventory { get; set; }

    [JsonPropertyName("progress")]
    public Progress Progress { get; set; }
}

public record Inventory
{
    [JsonPropertyName("upgrades")]
    public IEnumerable<Upgrade> Upgrades { get; set; }

    [JsonPropertyName("shards")]
    public IEnumerable<Shard> Shards { get; set; }

    [JsonPropertyName("mythicShards")]
    public IEnumerable<Shard> MythicShards { get; set; }

    [JsonPropertyName("xpBooks")]
    public IEnumerable<XpBook> XpBooks { get; set; }

    [JsonPropertyName("abilityBadges")]
    public AbilityBadges AbilityBadges { get; set; }

    [JsonPropertyName("components")]
    public IEnumerable<MoWComponent> Components { get; set; }

    [JsonPropertyName("forgeBadges")]
    public IEnumerable<ForgeBadge> ForgeBadges { get; set; }

    [JsonPropertyName("orbs")]
    public Orbs Orbs { get; set; }

    [JsonPropertyName("items")]
    public IEnumerable<InventoryEquipment> Items { get; set; }
}

public class Progress
{
    [JsonPropertyName("campaigns")]
    public List<CampaignProgress> Campaigns { get; set; }  // List of CampaignProgress objects

    [JsonPropertyName("guildRaid")]
    public GuildRaidTokenProgress GuildRaid { get; set; }

    [JsonPropertyName("arena")]
    public TokenProgress Arena { get; set; }

    [JsonPropertyName("onslaught")]
    public TokenProgress Onslaught { get; set; }

    [JsonPropertyName("salvageRun")]
    public TokenProgress SalvageRun { get; set; }

    [JsonPropertyName("legendaryEvents")]
    public List<LegendaryEvent> LegendaryEvents { get; set; }
}

public class Metadata
{
    [JsonPropertyName("configHash")]
    public string ConfigHash { get; set; }

    [JsonPropertyName("apiKeyExpiresOn")]
    public long? ApiKeyExpiresOn { get; set; }

    [JsonPropertyName("lastUpdatedOn")]
    public long LastUpdatedOn { get; set; }

    [JsonPropertyName("scopes")]
    public List<string> Scopes { get; set; }
}
