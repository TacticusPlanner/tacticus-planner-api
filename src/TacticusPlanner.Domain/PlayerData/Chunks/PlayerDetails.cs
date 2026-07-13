namespace TacticusPlanner.Domain.PlayerData.Chunks;

public sealed class PlayerDetailsChunk
{
    // The player's own display name — not catalog-inferable (there is no catalog id for a player).
    public string Name { get; set; } = string.Empty;

    public int PowerLevel { get; set; }
}
