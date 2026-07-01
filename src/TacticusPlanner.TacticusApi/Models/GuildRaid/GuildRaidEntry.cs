using TacticusPlanner.TacticusApi.Models.Shared;

namespace TacticusPlanner.TacticusApi.Models.GuildRaid;

public class GuildRaidEntry
{
    public Guid UserId { get; set; }
    public int Tier { get; set; }
    public int Set { get; set; }
    public int EncounterIndex { get; set; }
    public int RemainingHp { get; set; }
    public int MaxHp { get; set; }
    public EncounterType EncounterType { get; set; }
    public string UnitId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Rarity Rarity { get; set; }
    public int DamageDealt { get; set; }
    public DamageType DamageType { get; set; }
    public long? StartedOn { get; set; }
    public long? CompletedOn { get; set; }
    public List<GuildRaidUnit> HeroDetails { get; set; } = new();
    public GuildRaidUnit? MachineOfWarDetails { get; set; }
    public string GlobalConfigHash { get; set; } = string.Empty;
}
